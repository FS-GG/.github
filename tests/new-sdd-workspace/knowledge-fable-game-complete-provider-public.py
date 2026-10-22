#!/usr/bin/env python3
"""Published019 COMPLETE provider: explicit and genuinely omitted typed lifecycle.

Pure controls launch no children. Runtime needs separate root admission, sealed
public inputs and the accepted finite runner. Fixed ASCII parameters only.
"""
from __future__ import annotations
import argparse
import hashlib
import inspect
import json
import os
import re
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import time
import types
import unittest
from unittest.mock import Mock, patch
import zipfile

PLAYER_PROVIDER_SHA = '157a5841498a1e2873edb67731905deedd2ba158c60cae6043f42fb998373952'
ROWS = ({'id': 'provider-fable-complete-explicit-typed', 'product': 'ProviderCompleteTyped', 'explicit': True},
        {'id': 'provider-fable-complete-omitted-typed', 'product': 'ProviderCompleteOmitted', 'explicit': False})


def require(ok, reason):
    if not ok: raise ValueError(reason)


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_ports():
    path = Path(__file__).with_name('knowledge-fable-game-player-provider-public.py').resolve()
    require(digest(path) == PLAYER_PROVIDER_SHA, 'accepted-player-provider-ports-drift')
    module = types.ModuleType('sealed_complete_provider_ports'); module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


PL = load_ports()
V, L, P, M, C = PL.V, PL.L, PL.P, PL.M, PL.C
PACKAGES, TOP_KEYS, PINS = PL.PACKAGES, PL.TOP_KEYS, PL.PINS
LIFECYCLE_FILES, LIFECYCLE_DIRS = PL.LIFECYCLE_FILES, PL.LIFECYCLE_DIRS
IGNORE_PREFIX, IGNORE_SUFFIX = PL.IGNORE_PREFIX, PL.IGNORE_SUFFIX
hybrid_manifest, rendered_owner, matches_glob = PL.hybrid_manifest, PL.rendered_owner, PL.matches_glob
initial, restore_tools = PL.initial, PL.restore_tools
INITIAL_HASHES, INITIAL_SEED = PL.INITIAL_HASHES, PL.INITIAL_SEED
SUPPLIER_MANIFEST_HASHES = PL.SUPPLIER_MANIFEST_HASHES
# The producer root contract (Contracts.Schemas.agentSkillRoots), the pinned
# Commands assembly, seeded process skills and Foundation literals are the
# accepted PLAYER-provider pins and do not read the bundle parameter. The compiled
# supplier predicates DO: see COMPLETE_SUPPLIERS below.
AGENT_ROOTS, SCHEMAS_SHA = PL.AGENT_ROOTS, PL.SCHEMAS_SHA
COMMANDS_SHA, CONTRACTS_SHA = PL.COMMANDS_SHA, PL.CONTRACTS_SHA
authenticated_agent_roots = PL.authenticated_agent_roots
MANIFEST, PREFIX = PL.MANIFEST, PL.PREFIX

# Published Workspace.Template 0.19.0 fs-gg-fable-game modifier conditions. The
# packed template.json is separately authenticated by P.packed_contract.
ALL_MODIFIERS = frozenset({'(portalExample != true)', '(lifecycle != "typed-sdd")', '(bundle == "player")',
                           '((bundle != "tactical") && (bundle != "complete"))',
                           '((bundle != "arcade") && (bundle != "complete"))', '(bundle != "complete")', 'true'})
# COMPLETE with PortalExample absent (default false) and lifecycle typed-sdd:
# every bundle-specific modifier is unselected, so the bundle exclusions do not
# apply. PortalExample and the two preview files remain excluded.
COMPLETE_SELECTED = frozenset({'(portalExample != true)', 'true'})
# The accepted PLAYER selection, used only to derive the COMPLETE-only content.
PLAYER_SELECTED = frozenset({'(portalExample != true)', '(bundle == "player")',
                             '((bundle != "tactical") && (bundle != "complete"))',
                             '((bundle != "arcade") && (bundle != "complete"))',
                             '(bundle != "complete")', 'true'})
EXCLUDED_PACKAGE_FILES = ('.template.config/template.json', 'PortalExample/Consumer.fsproj',
                          'PortalExample/PortalScene.fs', 'PortalExample/Presentation.fs',
                          'PortalExample/Program.fs', 'PortalExample/README.md', 'PortalExample/manage.py',
                          'PortalExample/packages.lock.json', 'PortalExample/source-provenance.json',
                          'PortalExample/verify-package-boundary.py', 'SvgFoundation/PreviewDocument.fs',
                          'SvgFoundation/PreviewFont.fs')
APPLICABLE_FILE_COUNT = 171
COMPLETE_ONLY_COUNT = 53
# Fixed published package-relative selections; independent of the product name.
APPLICABLE_NAMES_SHA = 'a801747a08df06ee541aef5f6de945574168189216baa999467da959314b525e'
COMPLETE_ONLY_NAMES_SHA = '410a07e07b007f059404ccb6e02a4361baf223eca024209e2fc8e0ed0b7f9835'
COMPLETE_FILES = ('Conformance/GeneratedArenaRuleModel.fs',
                  'Conformance/GeneratedTacticalRuleModel.fs', 'SvgFoundation/Studio/Studio.fsproj',
                  'models/svg-arena/arena-rules.qnt', 'models/svg-tactical/tactical-rules.qnt',
                  'SvgFoundation/Examples/Tactical/scene.json', 'SvgFoundation/Examples/Arcade/scene.json',
                  'SvgFoundation/Examples/FourD/reference.json',
                  'SvgFoundation/Examples/ExternalAuthority/reference.json',
                  'SvgFoundation/Examples/Wasm/WasmPolicyExample.fs',
                  'SvgFoundation/Examples/SoldierReference/reference.json',
                  'SvgFoundation/Examples/SoldierReference/SoldierDocument.fs',
                  'SvgFoundation/Examples/SoldierReference/SoldierWorkload.fs')


# Compiled published SDD2.2.0 Commands manifests gate five more skills on
# `bundle in [..., complete]`. A supplied bundle=complete lands in the effective
# map the planners evaluate, so COMPLETE emits exactly these additions to the
# accepted PLAYER set. Game rows fold as `always` with no supplier; rendering rows
# retain predicate and supplier. Schema1 declares no per-file sets: SKILL.md only.
def _supplier(channel, id, sha, predicate, row_predicate, supplied):
    row = {'id': id, 'scope': 'product', 'sha256': sha, 'resolvablePath': '.agents/skills/' + id + '/SKILL.md',
           'materializes-when': row_predicate}
    if supplied: row['supplied-by'] = 'template/product-skills/' + id + '/'
    return {'channel': channel, 'sourcePredicate': predicate,
            'sourceSupplier': 'template/product-skills/' + id + '/', 'row': row, 'files': {'SKILL.md': sha}}


_RENDER = 'profile in [app, game] or template == fable-game and bundle in [studio, tactical, arcade, complete]'
COMPLETE_SUPPLIERS = {
    'fs-gg-rules': _supplier('GameSkill', 'fs-gg-rules', 'c656c3b73d946d4ff92d547a92fba22aadcdb72c0edb85b59942aa95f9abf6bf',
                             'template == fable-game and bundle in [tactical, complete]', 'always', False),
    'fs-gg-layout': _supplier('RenderingSkill', 'fs-gg-layout', '85c55e26ee42bd8d54533abcf6cb955d53e02651901be3c0e91bfe92dac92d62',
                              _RENDER, _RENDER, True),
    'fs-gg-styling': _supplier('RenderingSkill', 'fs-gg-styling', '24754ec5cb931deb6fcaf7d4b9e5926c42f4325dc4f67cb4852295de8b405ac9',
                               _RENDER, _RENDER, True),
    'fs-gg-ui-widgets': _supplier('RenderingSkill', 'fs-gg-ui-widgets', 'a975b5ec33c2fcf847a9075cf10c970090a2ed3728d19820baeffad352c9c44e',
                                  _RENDER, _RENDER, True),
    'fs-gg-svg-assets': _supplier('RenderingSkill', 'fs-gg-svg-assets', 'adccc361f6633585bf19218d94406bd15b1f001bf0d63d6ef40d821a7c6f4f82',
                                  'template == fable-game and bundle in [studio, tactical, arcade, complete]',
                                  'template == fable-game and bundle in [studio, tactical, arcade, complete]', True),
}
# Effective parameter maps seen by the compiled planners (profile absent).
PLAYER_PARAMS = {'template': 'fable-game', 'lifecycle': 'typed-sdd'}
COMPLETE_PARAMS = dict(PLAYER_PARAMS, bundle='complete')


def _predicate_atom(params, atom):
    atom = atom.strip()
    if atom == 'always': return True
    if atom == 'false': return False
    for token in (' in ', ' == ', ' != '):
        if token in atom:
            key, value = atom.split(token, 1); key, value = key.strip(), value.strip()
            current = params.get(key, '')
            if token == ' in ':
                require(value.startswith('[') and value.endswith(']'), 'complete-supplier-predicate')
                return current in {x.strip() for x in value[1:-1].split(',') if x.strip()}
            return (current == value) if token == ' == ' else (current != value)
    raise ValueError('complete-supplier-predicate')


def predicate_holds(predicate, params):
    # SDD ProductPredicate: ordinary precedence, `and` binds within an `or` clause;
    # an unset key reads as the empty string.
    return any(all(_predicate_atom(params, a) for a in clause.split(' and ')) for clause in predicate.split(' or '))


def embedded_manifests(raw):
    decoder, text, found, position = json.JSONDecoder(), raw.decode('utf-8', 'replace'), [], 0
    for opening in re.finditer(r'\{\s*"schemaVersion"', text):
        if opening.start() < position: continue
        try: value, position = decoder.raw_decode(text, opening.start())
        except ValueError: continue
        if isinstance(value, dict) and isinstance(value.get('skills'), list): found.append(value)
    return found


def authenticate_complete_suppliers(commands_dll):
    """Derive the COMPLETE supplier delta from the pinned compiled manifests."""
    require(hashlib.sha256(commands_dll).hexdigest() == COMMANDS_SHA, 'complete-published-supplier-assembly')
    rows = [r for man in embedded_manifests(commands_dll) for r in man['skills']]
    require(rows and all(isinstance(r.get('id'), str) and isinstance(r.get('materializes-when'), str) for r in rows),
            'complete-compiled-manifest-rows')
    def ids(params): return {r['id'] for r in rows if predicate_holds(r['materializes-when'], params)}
    player, complete = ids(PLAYER_PARAMS), ids(COMPLETE_PARAMS)
    require(player == set(PL.SUPPLIERS), 'complete-compiled-player-model-drift')
    require(complete - player == set(COMPLETE_SUPPLIERS) and player <= complete, 'complete-compiled-supplier-delta')
    for id, supplier in COMPLETE_SUPPLIERS.items():
        matches = [r for r in rows if r['id'] == id]
        require(len(matches) == 1, 'complete-compiled-supplier-row')
        row = matches[0]
        require(row['materializes-when'] == supplier['sourcePredicate'] and row['sha256'] == supplier['files']['SKILL.md']
                and row['scope'] == 'product' and row['resolvablePath'] == supplier['row']['resolvablePath']
                and row.get('supplied-by') == supplier['sourceSupplier']
                and [(f['path'], f['sha256']) for f in row['files']] == [('SKILL.md', row['sha256'])],
                'complete-compiled-supplier-row')
    return sorted(complete - player)


def skill_union(root, inventory, provenance):
    # The sealed PLAYER union check over the accepted PLAYER suppliers plus the
    # bundle-gated COMPLETE additions; no whole-manifest exemption is introduced.
    with patch.dict(PL.SUPPLIERS, COMPLETE_SUPPLIERS):
        return PL.skill_union(root, inventory, provenance)


def published_producer_contract(archive):
    with zipfile.ZipFile(archive) as z:
        authenticate_complete_suppliers(z.read('tools/net10.0/any/FS.GG.SDD.Commands.dll'))
        require(hashlib.sha256(z.read('tools/net10.0/any/FS.GG.Contracts.dll')).hexdigest() == CONTRACTS_SHA,
                'complete-published-contracts-assembly')


def names_sha(names): return hashlib.sha256('\n'.join(sorted(names)).encode()).hexdigest()


def descriptor_contract(path):
    # The immutable descriptor is selected directly, never a mutable registry.
    text = PL.descriptor_contract(path)
    # bundle is a declared optional provider parameter with no descriptor default;
    # the template's own player default is therefore never inferred here.
    require('      - key: bundle\n        required: false\n      - key: portalExample\n' in text,
            'provider-descriptor-bundle-contract')
    return text


def command(m, row, root):
    require(row in ROWS, 'unknown-complete-provider-subject')
    argv = [m['sddExecutable'], 'scaffold', '--root', root, '--provider', 'fable-game',
            '--no-update', '--text', '--param', 'productName=' + row['product'],
            '--param', 'bundle=complete']
    return argv + (['--param', 'lifecycle=typed-sdd'] if row['explicit'] else [])


def package_selection(z, selected):
    config = json.loads(z.read(PREFIX + '.template.config/template.json'))
    source, = config['sources']
    require(source['source'] == './' and source['target'] == './', 'complete-product-source-mapping')
    modifiers = source['modifiers']
    require(len({m['condition'] for m in modifiers}) == len(modifiers)
            and {m['condition'] for m in modifiers} == ALL_MODIFIERS, 'complete-selected-modifier-contract')
    excludes = source['exclude'] + [p for m in modifiers if m['condition'] in selected for p in m['exclude']]
    members = [i for i in z.infolist() if i.filename.startswith(PREFIX) and not i.is_dir()]
    require(len({i.filename for i in members}) == len(members), 'complete-duplicate-member')
    included, excluded = {}, []
    for info in members:
        name = info.filename[len(PREFIX):]
        require(not Path(name).is_absolute() and '..' not in Path(name).parts, 'complete-package-path')
        if any(matches_glob(name, pattern) for pattern in excludes): excluded.append(name)
        else: included[name] = info
    return included, excluded


def complete_only(archive):
    # Differential proof: content present for COMPLETE but removed by the accepted
    # PLAYER selection of the identical authenticated package definition.
    with zipfile.ZipFile(archive) as z:
        complete, _ = package_selection(z, COMPLETE_SELECTED)
        player, _ = package_selection(z, PLAYER_SELECTED)
    return sorted(set(complete) - set(player))


def product_inventory(archive, row):
    require(row in ROWS, 'unknown-complete-provider-subject')
    P.packed_contract(archive)
    with zipfile.ZipFile(archive) as z:
        included, excluded = package_selection(z, COMPLETE_SELECTED)
        require(sorted(excluded) == sorted(EXCLUDED_PACKAGE_FILES) and len(excluded) == len(EXCLUDED_PACKAGE_FILES),
                'complete-exact-exclusions')
        require(len(included) == APPLICABLE_FILE_COUNT and names_sha(included) == APPLICABLE_NAMES_SHA,
                'complete-exact-applicable-inventory')
        inventory = {}
        for name, info in included.items():
            target = rendered_owner(name.encode(), row['product']).decode()
            mode = (info.external_attr >> 16) & 0o777
            require(mode == 0o644, 'complete-public-product-mode:' + name)
            require(target not in inventory, 'complete-substitution-collision')
            inventory[target] = (rendered_owner(z.read(info), row['product']), mode | 0o111 if target.endswith('.sh') else mode)
        only = sorted(set(included) - set(package_selection(z, PLAYER_SELECTED)[0]))
        require(len(only) == COMPLETE_ONLY_COUNT and names_sha(only) == COMPLETE_ONLY_NAMES_SHA
                and set(COMPLETE_FILES) <= set(only), 'complete-only-content')
        require(set(L.OWNER_FILES) | set(COMPLETE_FILES) | {MANIFEST, 'SvgFoundation/SvgFoundation.fsproj'}
                <= set(inventory), 'complete-positive-inventory')
        excluded_targets = [rendered_owner(n.encode(), row['product']).decode() for n in excluded]
        return inventory, excluded_targets


def boundary(root, archive, row):
    original = P.packed_contract(archive)
    manifest = hybrid_manifest(root, original)
    inventory, excluded = product_inventory(archive, row)
    only = [rendered_owner(n.encode(), row['product']).decode() for n in complete_only(archive)]
    require(set(only) <= set(inventory) and all((root / name).is_file() for name in only), 'complete-only-owner-assets')
    require(all(not (root / name).exists() for name in excluded) and not (root / 'PortalExample').exists(),
            'complete-excluded-product-file')
    M.assets(root, archive, PREFIX)
    provenance = C.read_json(root / '.fsgg/scaffold-provenance.json')
    pairs = provenance['effectiveParameters']
    require(len({p['key'] for p in pairs}) == len(pairs), 'complete-effective-duplicates')
    effective = {p['key']: p['value'] for p in pairs}
    require(provenance['providerName'] == 'fable-game' and provenance['templateRef'] == 'fs-gg-fable-game'
            and effective['productName'] == row['product'] and effective['lifecycle'] == 'typed-sdd'
            and effective.get('bundle') == 'complete' and 'svgFoundation' not in effective
            and effective.get('portalExample', 'false') != 'true', 'complete-effective-provider-selection')
    require(all(p['path'] != '.config/dotnet-tools.json' for p in provenance['sddOwnedPaths']),
            'complete-whole-tool-manifest-ownership')
    skills = skill_union(root, inventory, provenance)
    owner = {}
    for name, (raw, mode) in inventory.items():
        target = root / name
        require(target.is_file() and not target.is_symlink() and stat.S_IMODE(target.stat().st_mode) == mode,
                'complete-owner-file-mode:' + name)
        if name in ('.config/dotnet-tools.json', MANIFEST): continue
        expected = IGNORE_PREFIX + raw + IGNORE_SUFFIX if name == '.gitignore' else raw
        require(target.read_bytes() == expected, 'complete-owner-bytes:' + name)
        owner[name] = hashlib.sha256(raw).hexdigest()
    for key in ('producedPaths', 'mirroredPaths', 'driverPaths', 'gameSkillPaths', 'renderingSkillPaths'):
        for item in provenance.get(key, []):
            name = item['path']; require(not Path(name).is_absolute() and '..' not in Path(name).parts, 'complete-provenance-path')
            path = root / name; require(path.is_file() and not path.is_symlink(), 'complete-provenance-file:' + name)
            if item.get('sha256'): require(digest(path) == item['sha256'], 'complete-provenance-digest:' + name)
            skills[name] = digest(path)
    canonical = sorted(p.relative_to(root).as_posix() for p in (root / '.fsgg/knowledge').rglob('*') if p.is_file())
    require('.fsgg/knowledge/schema.json' in canonical and any('/records/' in p for p in canonical)
            and (root / '.fsgg/knowledge-guide.md').stat().st_size > 0
            and all((root / name).is_file() and (root / name).stat().st_size > 0 for name in LIFECYCLE_FILES)
            and all((root / name).is_dir() for name in LIFECYCLE_DIRS), 'complete-automatic-typed-knowledge')
    for name, sha in INITIAL_HASHES.items():
        target = root / name
        require(target.is_file() and not target.is_symlink() and digest(target) == sha
                and stat.S_IMODE(target.stat().st_mode) == 0o644, 'complete-published-initial-file:' + name)
    seed_path = root / '.fsgg/knowledge/records/project-knowledge.json'
    require(seed_path.is_file() and not seed_path.is_symlink()
            and stat.S_IMODE(seed_path.stat().st_mode) == 0o644, 'complete-published-initial-seed-mode')
    require(canonical == ['.fsgg/knowledge/records/project-knowledge.json', '.fsgg/knowledge/schema.json']
            and PL.V.exact_json(C.read_json(seed_path)) == PL.V.exact_json(INITIAL_SEED),
            'complete-published-initial-seed')
    return dict(effectiveParameters=effective, bundle='complete', selectorPrecedence=100,
                svgFoundationParameterOmitted=True, lifecycleOmitted=not row['explicit'],
                automaticInitialization=True, standaloneInitializeInvoked=False, knowledgeReady=True,
                ownerAssets=owner, manifest=manifest, skills=skills, emittedSkillRoots=list(AGENT_ROOTS),
                publishedSchemasSha256=SCHEMAS_SHA, applicableFileCount=len(inventory),
                excludedFileCount=len(excluded), completeOnlyFiles=sorted(only),
                productFiles=sorted(inventory), excludedProductFiles=sorted(excluded), canonicalFiles=canonical,
                lifecycleFiles=list(LIFECYCLE_FILES), supplierManifestHashes=SUPPLIER_MANIFEST_HASHES,
                provenanceSha256=digest(root / '.fsgg/scaffold-provenance.json'))


def complete_initial(runner, root, archive, row, observed):
    result = initial(runner, root, archive, row, observed)
    # Every complete-only file is part of the committed bounded blob/tree proof.
    require(set(observed['completeOnlyFiles']) <= set(result['gitBlobs']), 'complete-only-initial-blob-coverage')
    return result


def validate(path, expected_sha, after=False):
    require(expected_sha and digest(path) == expected_sha, 'manifest-digest')
    m = C.read_json(path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.provider-fable-complete-knowledge/1', 'closed-provider-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-public-provider-packages')
    for name in m['inputs']: C.regular(name, m['inputs'])
    C.regular(m['runnerPort'], m['inputs'])
    for name in (__file__, PL.__file__, V.__file__, L.__file__, P.__file__, M.__file__, C.__file__):
        C.regular(Path(name).resolve(), m['inputs'])
    for relative, sha in C.SOURCE_HASHES.items():
        if relative.startswith('tests/knowledge-receiver/'):
            require(digest(C.regular(Path(m['sourceRoot']) / relative, m['inputs'])) == sha, 'frozen-provider-probe-source')
    for key, identity in PACKAGES.items():
        binding = m['packages'][key]
        require(set(binding) == ({'archive', 'publicationReceipts', 'closure'} if key == 'producer'
                                else {'archive', 'publicationReceipts'}), 'closed-provider-package-binding')
        members = M.public_package(key, binding, m['inputs'], identity)
        if key == 'producer': C.tool_closure(binding, m['sddExecutable'], m['inputs'], members, 'FS.GG.SDD.Cli.dll')
    P.packed_contract(m['packages']['workspace']['archive'])
    published_producer_contract(m['packages']['producer']['archive'])
    roots = authenticated_agent_roots(C.regular(m['publishedSchemas'], m['inputs']).read_bytes())
    require(roots == AGENT_ROOTS, 'complete-derived-agent-root-drift')
    descriptor_contract(C.regular(m['providerDescriptor'], m['inputs']))
    env = m['childEnvironment']
    require(set(env) == {'PATH', *M.SERIAL_ENV, *M.HOST_ENV}
            and all(env[k] == v for k, v in {**M.SERIAL_ENV, **M.HOST_ENV}.items()), 'closed-provider-host-environment')
    require(m['actualHome'] == os.environ.get('HOME'), 'actual-home')
    for name, expected in (('dotnet', m['dotnetExecutable']), ('fsgg-sdd', m['sddExecutable'])):
        selected = shutil.which(name, path=env['PATH'])
        require(selected and Path(selected).resolve() == Path(expected).resolve(), 'selected-provider-executable')
        C.regular(Path(expected), m['inputs'])
    git = shutil.which('git', path=env['PATH']); require(git, 'missing-bound-git')
    for executable in (Path(git).resolve(), Path(sys.executable).resolve()): C.regular(executable, m['inputs'])
    root = Path(m['workRoot'])
    require(root.is_absolute() and root == root.resolve() and not root.is_symlink()
            and Path(m['report']).parent == root and (after or not root.exists()), 'fresh-provider-output-no-replay')
    return m


def qualify(path, expected_sha, *, runner_factory=None):
    require(callable(runner_factory), 'qualified-runner-factory-required')
    started = time.monotonic(); deadline = started + 600
    m = validate(path, expected_sha)
    require(time.monotonic() < deadline - 9, 'setup-exhausted-original-window')
    base = Path(m['workRoot']); base.mkdir(mode=0o700)
    for name in ('tmp', 'dotnet-home', 'packages', 'http-cache'): (base / name).mkdir()
    feed = base / 'feed'; feed.mkdir()
    for key, identity in PACKAGES.items():
        shutil.copyfile(m['packages'][key]['archive'], feed / (identity[0] + '.' + identity[1] + '.nupkg'))
    require(not any(c in str(feed) for c in '<>&"'), 'unsafe-local-feed-path')
    config = base / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="sealed-public" value="' + str(feed) + '"/></packageSources></configuration>')
    runner = P.injected_runner(runner_factory, m, base, deadline - 5)
    report = dict(schema='fsgg.provider-fable-complete-knowledge-result/1', state='started', manifestSha256=expected_sha,
                  outerSeconds=600, perCommandMaxSeconds=180, rows=[], runtimeAdmission='external-required',
                  unfulfilled=['independent full-result acceptance', 'other parent4642 routes and retained upgrades',
                               'product/browser/WASM/Wizard/publication acceptance'])
    def save(): Path(m['report']).write_text(json.dumps(report, indent=2) + '\n')
    save()
    try:
        require(runner.call([m['sddExecutable'], '--version']).stdout.strip() == '2.2.0', 'actual-public-provider-producer')
        for row in ROWS:
            case = base / row['id']; case.mkdir()
            for name in ('tmp', 'dotnet-home', 'packages', 'http-cache'): (case / name).mkdir()
            runner.env = M.env_for(m, case); root = case / 'receiver'
            entry = dict(id=row['id'], state='started'); report['rows'].append(entry); save()
            descriptor = root / '.fsgg/providers.yml'; descriptor.parent.mkdir(parents=True)
            descriptor.write_bytes(Path(m['providerDescriptor']).read_bytes())
            runner.call([m['dotnetExecutable'], 'new', 'install', m['packages']['workspace']['archive']], cwd=case)
            runner.call(command(m, row, root), cwd=case)
            entry['registration'] = C.registration(case, '0.19.0', PACKAGES['workspace'][3])
            entry['provider'] = boundary(root, m['packages']['workspace']['archive'], row)
            before = {k: v for k, v in M.snapshot(root).items() if k in entry['provider']['productFiles']}
            ignore = (root / '.gitignore').read_bytes()
            entry['initial'] = complete_initial(runner, root, m['packages']['workspace']['archive'], row, entry['provider']); save()
            restore_tools(runner, m, root, config)
            entry['knowledge'] = P.knowledge(runner, m, root, case)
            require(entry['knowledge']['probe']['probeSetupCommit'] != entry['initial']['commit'], 'provider-probe-not-caller-initial')
            P.preserved(before, root, ignore_prefix=ignore)
            hybrid_manifest(root, P.packed_contract(m['packages']['workspace']['archive']))
            entry.update(state='passed', knowledgeReady=True); save()
        require(validate(path, expected_sha, after=True) == m, 'post-provider-custody-drift')
        require(time.monotonic() < deadline, 'original-provider-deadline')
        report.update(state='passed', commands=runner.sequence, elapsedSeconds=time.monotonic() - started)
        save(); return report
    except BaseException as error:
        if report['rows'] and report['rows'][-1]['state'] == 'started': report['rows'][-1]['state'] = 'failed'
        report.update(state='failed', commands=runner.sequence, failureType=type(error).__name__, failure=str(error))
        try: save()
        except BaseException as custody_error: error.add_note('failure-report-custody-Unknown:' + type(custody_error).__name__)
        raise


def source_controls():
    real_suppliers = dict(COMPLETE_SUPPLIERS)

    class Controls(unittest.TestCase):
        CONDITIONS = ['(portalExample != true)', '(lifecycle != "typed-sdd")', '(bundle == "player")',
                      '((bundle != "tactical") && (bundle != "complete"))',
                      '((bundle != "arcade") && (bundle != "complete"))', '(bundle != "complete")', 'true']
        EXCLUSIONS = [['PortalExample/**'], list(M.CI_FILES),
                      ['SvgFoundation/Studio/**', 'models/**', 'Conformance/GeneratedArenaRuleModel.fs',
                       'Conformance/GeneratedTacticalRuleModel.fs'],
                      ['SvgFoundation/Examples/Tactical/**'], ['SvgFoundation/Examples/Arcade/**'],
                      ['SvgFoundation/Examples/Wasm/**', 'SvgFoundation/Examples/FourD/**',
                       'SvgFoundation/Examples/ExternalAuthority/**', 'SvgFoundation/Examples/SoldierReference/**'],
                      ['SvgFoundation/PreviewDocument.fs', 'SvgFoundation/PreviewFont.fs']]
        EXCLUDED = ['.template.config/template.json', 'PortalExample/Program.fs',
                    'SvgFoundation/PreviewDocument.fs', 'SvgFoundation/PreviewFont.fs']

        def setUp(self):
            body = b'fixture supplier\n'; sha = hashlib.sha256(body).hexdigest(); self.body = body
            suppliers = {'fixture-driver': dict(channel='Driver', sourcePredicate='always', sourceSupplier='fixture',
                row={'id': 'fixture-driver', 'scope': 'driver', 'sha256': sha, 'resolvablePath': '.agents/skills/fixture-driver/SKILL.md', 'materializes-when': 'always'},
                files={'SKILL.md': sha, 'references/control.md': sha})}
            extra = {'fixture-complete': dict(channel='RenderingSkill', sourcePredicate='bundle in [complete]', sourceSupplier='template/product-skills/fixture-complete/',
                row={'id': 'fixture-complete', 'scope': 'product', 'sha256': sha, 'resolvablePath': '.agents/skills/fixture-complete/SKILL.md', 'materializes-when': 'bundle in [complete]',
                     'supplied-by': 'template/product-skills/fixture-complete/'}, files={'SKILL.md': sha})}
            for target, value in ((PL.SEEDED_SKILLS, {'fs-gg-sdd-fixture': sha}), (INITIAL_HASHES, {}), (PL.SUPPLIERS, suppliers),
                                  (COMPLETE_SUPPLIERS, extra)):
                seal = patch.dict(target, value, clear=True); seal.start(); self.addCleanup(seal.stop)
            packed = patch.object(P, 'packed_contract'); self.packed = packed.start(); self.addCleanup(packed.stop)

        def build(self, td, *, row=ROWS[0], files=None, conditions=None, drop=(), modes=None):
            """Fixture package + receiver for the COMPLETE selection; pins are patched to its construction."""
            td = Path(td); root = td / 'receiver'; root.mkdir(); archive = td / 'fixture.nupkg'
            original = {'version': 1, 'isRoot': True, 'x-root': False, 'tools': {
                'fable': {'version': '5.18.0', 'commands': ['fable'], 'unknown': [False, 1]},
                'fantomas': {'version': '8.0.5', 'commands': ['fantomas'], 'rollForward': False}}}
            self.packed.return_value = original
            owner = b'owner skill\n'; osha = hashlib.sha256(owner).hexdigest()
            rows = [{'id': 'owner', 'scope': 'product', 'sha256': osha, 'resolvablePath': '.agents/skills/owner/SKILL.md', 'materializes-when': 'template in [fable-game]', 'supplied-by': 'template/owner/'},
                    {'id': 'fable-bindings', 'scope': 'product', 'sha256': 'a' * 64, 'resolvablePath': '.agents/skills/fable-bindings/SKILL.md', 'materializes-when': 'template in [fable-bindings]', 'supplied-by': 'template/bindings/'}]
            conditions = conditions or self.CONDITIONS
            config = {'sources': [{'source': './', 'target': './', 'exclude': ['**/.template.config/**'],
                'modifiers': [{'condition': c, 'exclude': e} for c, e in zip(conditions, self.EXCLUSIONS + [[]])]}]}
            package = {n: ('owner ' + n + ' FableGameWorkspaceNamespace\n').encode() for n in
                       (*L.OWNER_FILES, *COMPLETE_FILES, 'SvgFoundation/SvgFoundation.fsproj', 'Client/Api.fs',
                        'Protocol/Http.fs', 'FableGameWorkspace.slnx', 'deploy/run-local.sh',
                        'PortalExample/Program.fs', 'SvgFoundation/PreviewDocument.fs', 'SvgFoundation/PreviewFont.fs')}
            for name in M.CI_FILES: package[name] = b'central CI fixture\n'
            package.update({'.template.config/template.json': json.dumps(config).encode(),
                            '.config/dotnet-tools.json': json.dumps(original).encode(), '.gitignore': b'owner-ignore\n',
                            MANIFEST: json.dumps({'schemaVersion': 1, 'skills': rows}).encode(),
                            '.agents/skills/owner/SKILL.md': owner, '.codex/skills/owner/SKILL.md': owner,
                            '.codex/README.md': b'product-owned Codex guidance\n'})
            # Accepted pins are the independently known construction of the
            # unmodified fixture; drifted variants keep these same pins.
            applicable = sorted(n for n in package if n not in self.EXCLUDED)
            package.update(files or {})
            for name in drop: package.pop(name)
            with zipfile.ZipFile(archive, 'w') as z:
                for name, raw in package.items():
                    info = zipfile.ZipInfo(PREFIX + name); info.external_attr = (stat.S_IFREG | (modes or {}).get(name, 0o644)) << 16; z.writestr(info, raw)
            pins = {'EXCLUDED_PACKAGE_FILES': tuple(self.EXCLUDED), 'APPLICABLE_FILE_COUNT': len(applicable),
                    'APPLICABLE_NAMES_SHA': names_sha(applicable), 'COMPLETE_ONLY_COUNT': len(COMPLETE_FILES),
                    'COMPLETE_ONLY_NAMES_SHA': names_sha(COMPLETE_FILES)}
            seal = patch.dict(globals(), pins); seal.start(); self.addCleanup(seal.stop)
            inventory, _ = product_inventory(archive, row)
            for name, (raw, mode) in inventory.items():
                p = root / name; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(raw); p.chmod(mode)
            (root / '.config/dotnet-tools.json').write_text(json.dumps(dict(original, tools=dict(original['tools'], **V.OWNED_TOOLS))))
            (root / '.gitignore').write_bytes(IGNORE_PREFIX + b'owner-ignore\n' + IGNORE_SUFFIX)
            suppliers = {**PL.SUPPLIERS, **COMPLETE_SUPPLIERS}
            allrows = rows + [s['row'] for s in suppliers.values()]
            prov = {'providerName': 'fable-game', 'templateRef': 'fs-gg-fable-game', 'sddOwnedPaths': [], 'producedPaths': [], 'mirroredPaths': [],
                    'effectiveParameters': [{'key': k, 'value': v} for k, v in {'productName': row['product'], 'bundle': 'complete', 'lifecycle': 'typed-sdd'}.items()]}
            for agent in AGENT_ROOTS:
                p = root / agent / 'skills/fs-gg-sdd-fixture/SKILL.md'; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(self.body)
                for id, supplier in suppliers.items():
                    for relative in supplier['files']:
                        p = root / agent / 'skills' / id / relative; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(self.body)
                p = root / agent / 'skills/owner/SKILL.md'; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(owner)
                p = root / agent / 'skills/skill-manifest.json'; p.write_text(json.dumps({'schemaVersion': 1, 'skills': allrows}))
                prov['producedPaths' if agent == '.agents' else 'mirroredPaths'].append({'path': p.relative_to(root).as_posix(), 'sha256': digest(p)})
            for name in ('.fsgg/knowledge/schema.json', '.fsgg/knowledge-guide.md', '.fsgg/providers.yml', *LIFECYCLE_FILES):
                p = root / name; p.parent.mkdir(parents=True, exist_ok=True); p.write_text('fixture')
            (root / '.fsgg/knowledge/records').mkdir(parents=True, exist_ok=True)
            (root / '.fsgg/knowledge/records/project-knowledge.json').write_text(json.dumps(INITIAL_SEED))
            for name in LIFECYCLE_DIRS: (root / name).mkdir()
            self.prov_path = root / '.fsgg/scaffold-provenance.json'; self.prov_path.write_text(json.dumps(prov))
            INITIAL_HASHES.update({n: digest(root / n) for n in (*LIFECYCLE_FILES, '.fsgg/knowledge-guide.md', '.fsgg/knowledge/schema.json')})
            return root, archive, inventory, prov

        def test_fixed_rows_bundle_complete_and_lifecycle_only_omission(self):
            for row in ROWS:
                args = command({'sddExecutable': '/sdd'}, row, Path('/fresh'))
                self.assertEqual(args[1], 'scaffold'); self.assertEqual(args.count('--text'), 1)
                self.assertEqual(args.count('bundle=complete'), 1); self.assertNotIn('--bundle', args)
                self.assertFalse(any(str(a).startswith('svgFoundation=') for a in args))
                self.assertEqual('lifecycle=typed-sdd' in args, row['explicit'])
            with self.assertRaisesRegex(ValueError, 'unknown-complete-provider'):
                command({}, dict(ROWS[0], explicit=False), Path('/fresh'))

        def test_descriptor_declares_optional_bundle_without_default(self):
            good = ('    parameters:\n      - key: productName\n        required: true\n'
                    '      - key: bundle\n        required: false\n      - key: portalExample\n        required: false\n')
            with tempfile.TemporaryDirectory() as td:
                path = Path(td) / 'descriptor'
                for text, ok in ((good, True), (good.replace('      - key: bundle\n        required: false\n', ''), False),
                                 (good.replace('required: false\n      - key: portalExample', 'required: false\n        default: complete\n      - key: portalExample'), False)):
                    with patch.object(PL, 'descriptor_contract', return_value=text):
                        if ok: self.assertEqual(descriptor_contract(path), text)
                        else:
                            with self.assertRaisesRegex(ValueError, 'descriptor-bundle-contract'): descriptor_contract(path)

        def test_complete_inventory_exclusions_and_only_content_are_exact(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td)
                self.assertEqual(sorted(complete_only(a)), sorted(COMPLETE_FILES))
                for name in COMPLETE_FILES: self.assertIn(name, i)
                self.assertIn(ROWS[0]['product'] + '.slnx', i); self.assertIn('.codex/skills/owner/SKILL.md', i)
                self.assertNotIn('PortalExample/Program.fs', i); self.assertNotIn('SvgFoundation/PreviewFont.fs', i)
                result = boundary(root, a, ROWS[0])
                self.assertEqual(result['bundle'], 'complete'); self.assertEqual(result['excludedFileCount'], len(self.EXCLUDED))
                self.assertEqual(sorted(result['completeOnlyFiles']), sorted(COMPLETE_FILES))
                self.assertTrue(result['automaticInitialization'] and not result['standaloneInitializeInvoked'])

        def test_package_drift_refuses_unpinned_inventory_and_selection(self):
            with tempfile.TemporaryDirectory() as td:
                cases = (('extra', dict(files={'Extra/New.fs': b'x'}), 'exact-applicable-inventory'),
                         ('missing', dict(drop=['SvgFoundation/Examples/Wasm/WasmPolicyExample.fs']), 'exact-applicable-inventory'),
                         ('unexcluded', dict(drop=['PortalExample/Program.fs']), 'exact-exclusions'),
                         ('same-count-rename', dict(files={'SvgFoundation/Examples/Wasm/Renamed.fs': b'x'}, drop=['SvgFoundation/Examples/Wasm/WasmPolicyExample.fs']), 'exact-applicable-inventory'),
                         ('duplicate-modifier', dict(conditions=self.CONDITIONS + ['true']), 'selected-modifier-contract'),
                         ('public-mode', dict(modes={'Client/Api.fs': 0o755}), 'public-product-mode'),
                         ('renamed', dict(conditions=self.CONDITIONS[:-1] + ['true  ']), 'selected-modifier-contract'),
                         ('swapped', dict(conditions=self.CONDITIONS[:5] + ['(bundle == "complete")', 'true']), 'selected-modifier-contract'))
                for name, kwargs, reason in cases:
                    d = Path(td) / name; d.mkdir()
                    with self.assertRaisesRegex(ValueError, reason): self.build(d, **kwargs)

        def test_complete_only_content_missing_changed_mode_and_excluded_present(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); boundary(root, a, ROWS[0])
                for name in ('SvgFoundation/Studio/Studio.fsproj', 'models/svg-arena/arena-rules.qnt',
                             'SvgFoundation/Examples/Wasm/WasmPolicyExample.fs', 'SvgFoundation/Examples/FourD/reference.json',
                             'SvgFoundation/Examples/SoldierReference/SoldierWorkload.fs',
                             'Conformance/GeneratedTacticalRuleModel.fs'):
                    target = root / name; raw = target.read_bytes(); mode = stat.S_IMODE(target.stat().st_mode)
                    target.unlink()
                    with self.assertRaisesRegex(ValueError, 'complete-only-owner-assets'): boundary(root, a, ROWS[0])
                    target.write_bytes(raw[:-1] + b'!')
                    with self.assertRaisesRegex(ValueError, 'owner-bytes'): boundary(root, a, ROWS[0])
                    target.write_bytes(raw); target.chmod(0o755)
                    with self.assertRaisesRegex(ValueError, 'owner-file-mode'): boundary(root, a, ROWS[0])
                    target.chmod(mode)
                for name in ('PortalExample/Program.fs', 'SvgFoundation/PreviewFont.fs', 'SvgFoundation/PreviewDocument.fs'):
                    target = root / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(b'excluded')
                    with self.assertRaisesRegex(ValueError, 'excluded-product-file'): boundary(root, a, ROWS[0])
                    target.unlink()
                shutil.rmtree(root / 'PortalExample')
                # A stray file under the managed opt-in Portal tree is refused even when no packaged name matches.
                target = root / 'PortalExample/Unlisted.fs'; target.parent.mkdir(); target.write_bytes(b'unlisted')
                with self.assertRaisesRegex(ValueError, 'excluded-product-file'): boundary(root, a, ROWS[0])
                shutil.rmtree(root / 'PortalExample'); boundary(root, a, ROWS[0])

        def test_product_codex_assets_preserved_without_union_manifest(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); boundary(root, a, ROWS[0])
                self.assertFalse((root / '.codex/skills/skill-manifest.json').exists())
                target = root / '.codex/skills/owner/SKILL.md'; raw = target.read_bytes(); target.write_bytes(b'changed owner')
                with self.assertRaisesRegex(ValueError, 'owner-bytes'): boundary(root, a, ROWS[0])
                target.write_bytes(raw); target.chmod(0o755)
                with self.assertRaisesRegex(ValueError, 'owner-file-mode'): boundary(root, a, ROWS[0])

        def test_owner_modes_and_narrow_shell_promotion(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); boundary(root, a, ROWS[0])
                self.assertEqual(stat.S_IMODE((root / 'deploy/run-local.sh').stat().st_mode), 0o755)
                self.assertEqual(stat.S_IMODE((root / 'SvgFoundation/Studio/Studio.fsproj').stat().st_mode), 0o644)
                for name in ('Client/Api.fs', 'deploy/run-local.sh', 'SvgFoundation/Examples/Arcade/scene.json'):
                    path = root / name; mode = stat.S_IMODE(path.stat().st_mode); path.chmod(0o600)
                    with self.assertRaisesRegex(ValueError, 'owner-file-mode'): boundary(root, a, ROWS[0])
                    path.chmod(mode)

        def test_effective_selection_requires_bundle_complete_without_svg_or_portal(self):
            changes = (lambda e: [x for x in e if x['key'] != 'bundle'],
                       lambda e: [dict(x, value='player') if x['key'] == 'bundle' else x for x in e],
                       lambda e: e + [{'key': 'svgFoundation', 'value': 'false'}],
                       lambda e: e + [{'key': 'portalExample', 'value': 'true'}],
                       lambda e: e + [{'key': 'bundle', 'value': 'complete'}],
                       lambda e: [dict(x, value='sdd') if x['key'] == 'lifecycle' else x for x in e])
            for change in changes:
                with tempfile.TemporaryDirectory() as td:
                    root, a, i, p = self.build(td); p['effectiveParameters'] = change(p['effectiveParameters'])
                    self.prov_path.write_text(json.dumps(p))
                    with self.assertRaisesRegex(ValueError, 'effective'): boundary(root, a, ROWS[0])

        def test_lifecycle_artifacts_are_required_and_omission_is_only_the_command_pair(self):
            for row in ROWS:
                with tempfile.TemporaryDirectory() as td:
                    root, a, i, p = self.build(td, row=row); result = boundary(root, a, row)
                    self.assertEqual(result['lifecycleOmitted'], not row['explicit'])
                    self.assertEqual(result['effectiveParameters']['lifecycle'], 'typed-sdd')
                    for name in LIFECYCLE_FILES:
                        target = root / name; raw = target.read_bytes(); target.unlink()
                        with self.assertRaises((ValueError, FileNotFoundError)): boundary(root, a, row)
                        target.write_bytes(raw)

        def test_exact_union_supplier_files_and_seeded_skills_follow_published_roots(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); skill_union(root, i, p)
                target = root / '.claude/skills/skill-manifest.json'; raw = target.read_bytes()
                value = json.loads(raw); value['skills'].pop(); target.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError, 'exact-supplier-union'): boundary(root, a, ROWS[0])
                target.write_bytes(raw)
                for agent in AGENT_ROOTS:
                    for relative in ('SKILL.md', 'references/control.md'):
                        t = root / agent / 'skills/fixture-driver' / relative; data = t.read_bytes(); t.write_bytes(data[:-1] + b'!')
                        with self.assertRaisesRegex(ValueError, 'supplier-file'): boundary(root, a, ROWS[0])
                        t.write_bytes(data)
                    t = root / agent / 'skills/fs-gg-sdd-fixture/SKILL.md'; data = t.read_bytes(); t.unlink()
                    with self.assertRaisesRegex(ValueError, 'seeded-skill'): boundary(root, a, ROWS[0])
                    t.write_bytes(data)
                t = root / '.agents/skills/fs-gg-sdd-invented/SKILL.md'; t.parent.mkdir(); t.write_text('dangling')
                with self.assertRaisesRegex(ValueError, 'dangling-or-unexpected'): boundary(root, a, ROWS[0])
                shutil.rmtree(t.parent)
                p['mirroredPaths'][0]['sha256'] = '0' * 64; self.prov_path.write_text(json.dumps(p))
                with self.assertRaisesRegex(ValueError, 'amended-manifest-provenance'): boundary(root, a, ROWS[0])

        def test_bundle_gated_supplier_delta_is_in_union_and_every_root(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); boundary(root, a, ROWS[0])
                for agent in AGENT_ROOTS:
                    target = root / agent / 'skills/fixture-complete/SKILL.md'; raw = target.read_bytes(); target.unlink()
                    with self.assertRaisesRegex(ValueError, 'supplier-file'): boundary(root, a, ROWS[0])
                    target.write_bytes(raw)
                manifest = root / '.agents/skills/skill-manifest.json'; value = json.loads(manifest.read_bytes())
                value['skills'] = [r for r in value['skills'] if r['id'] != 'fixture-complete']; manifest.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError, 'exact-supplier-union'): boundary(root, a, ROWS[0])
            # The union must not borrow the COMPLETE delta permanently.
            self.assertNotIn('fs-gg-rules', PL.SUPPLIERS)

        def test_real_complete_supplier_constants_follow_channel_projection(self):
            self.assertEqual(sorted(real_suppliers), ['fs-gg-layout', 'fs-gg-rules', 'fs-gg-styling', 'fs-gg-svg-assets', 'fs-gg-ui-widgets'])
            rules = real_suppliers['fs-gg-rules']
            self.assertEqual((rules['channel'], rules['row']['materializes-when'], 'supplied-by' in rules['row']), ('GameSkill', 'always', False))
            for id, supplier in real_suppliers.items():
                self.assertEqual(supplier['row']['resolvablePath'], '.agents/skills/' + id + '/SKILL.md')
                self.assertEqual(supplier['files'], {'SKILL.md': supplier['row']['sha256']})
                if id != 'fs-gg-rules':
                    self.assertEqual((supplier['channel'], supplier['row']['materializes-when'], supplier['row']['supplied-by']),
                                     ('RenderingSkill', supplier['sourcePredicate'], 'template/product-skills/' + id + '/'))

        def test_predicate_semantics_match_sdd_precedence_and_unset_keys(self):
            predicate = 'profile in [app, game] or template == fable-game and bundle in [studio, complete]'
            self.assertFalse(predicate_holds(predicate, PLAYER_PARAMS))
            self.assertTrue(predicate_holds(predicate, COMPLETE_PARAMS))
            self.assertTrue(predicate_holds(predicate, {'profile': 'game'}))
            self.assertFalse(predicate_holds(predicate, {'template': 'fable-game', 'bundle': 'player'}))
            self.assertTrue(predicate_holds('bundle != complete', PLAYER_PARAMS))
            with self.assertRaisesRegex(ValueError, 'supplier-predicate'): predicate_holds('nonsense', {})

        def test_supplier_delta_derives_from_the_pinned_compiled_manifests(self):
            def man(*rows): return json.dumps({'schemaVersion': 1, 'skills': list(rows)}, indent=2)
            def row(id, when, sha='a' * 64, supplied=None):
                value = {'id': id, 'scope': 'product', 'sha256': sha, 'resolvablePath': '.agents/skills/' + id + '/SKILL.md',
                         'materializes-when': when, 'files': [{'path': 'SKILL.md', 'sha256': sha}]}
                if supplied: value['supplied-by'] = supplied
                return value
            supplier = COMPLETE_SUPPLIERS['fixture-complete']
            sha = supplier['files']['SKILL.md']
            good = row('fixture-complete', 'bundle in [complete]', sha, 'template/product-skills/fixture-complete/')
            def dll(*rows, noise=b'\x00junk{"schemaVersion"\x00'):
                body = noise + b'{"schemaVersion":1,"exceptions":[]}' + man(*rows).encode()
                return body
            base = row('fixture-driver', 'always')
            with patch.object(PL, 'SUPPLIERS', {'fixture-driver': {}}):
                raw = dll(base, good)
                with patch(__name__ + '.COMMANDS_SHA', hashlib.sha256(raw).hexdigest()):
                    self.assertEqual(authenticate_complete_suppliers(raw), ['fixture-complete'])
                    with self.assertRaisesRegex(ValueError, 'supplier-assembly'): authenticate_complete_suppliers(raw + b' ')
                for rows, reason in (
                        ((base, good, row('fs-gg-hidden', 'bundle in [complete]')), 'compiled-supplier-delta'),
                        ((base, good, row('fs-gg-extra-player', 'always')), 'compiled-player-model-drift'),
                        ((base, dict(good, sha256='b' * 64)), 'compiled-supplier-row'),
                        ((base, dict(good, **{'supplied-by': 'template/elsewhere/'})), 'compiled-supplier-row'),
                        ((base, dict(good, **{'materializes-when': 'bundle in [complete, studio]'})), 'compiled-supplier-row'),
                        ((base,), 'compiled-supplier-delta')):
                    raw = dll(*rows)
                    with patch(__name__ + '.COMMANDS_SHA', hashlib.sha256(raw).hexdigest()):
                        with self.assertRaisesRegex(ValueError, reason): authenticate_complete_suppliers(raw)

        def test_producer_archive_contract_binds_both_assemblies(self):
            # validate is only exercised end to end by the admitted runtime; pin that it
            # routes the producer archive through this contract before any child runs.
            self.assertIn("published_producer_contract(m['packages']['producer']['archive'])", inspect.getsource(validate))
            manifests = json.dumps({'schemaVersion': 1, 'skills': [
                {'id': 'fixture-driver', 'scope': 'driver', 'sha256': 'a' * 64, 'resolvablePath': 'x', 'materializes-when': 'always', 'files': []},
                {'id': 'fixture-complete', 'scope': 'product', 'sha256': COMPLETE_SUPPLIERS['fixture-complete']['files']['SKILL.md'],
                 'resolvablePath': '.agents/skills/fixture-complete/SKILL.md', 'materializes-when': 'bundle in [complete]',
                 'supplied-by': 'template/product-skills/fixture-complete/',
                 'files': [{'path': 'SKILL.md', 'sha256': COMPLETE_SUPPLIERS['fixture-complete']['files']['SKILL.md']}]}]}).encode()
            contracts = b'fixture contracts'
            with tempfile.TemporaryDirectory() as td, patch.object(PL, 'SUPPLIERS', {'fixture-driver': {}}):
                archive = Path(td) / 'producer.nupkg'
                def write(commands, contract):
                    with zipfile.ZipFile(archive, 'w') as z:
                        z.writestr('tools/net10.0/any/FS.GG.SDD.Commands.dll', commands)
                        z.writestr('tools/net10.0/any/FS.GG.Contracts.dll', contract)
                with patch(__name__ + '.COMMANDS_SHA', hashlib.sha256(manifests).hexdigest()), \
                        patch(__name__ + '.CONTRACTS_SHA', hashlib.sha256(contracts).hexdigest()):
                    write(manifests, contracts); published_producer_contract(archive)
                    write(manifests, contracts + b'!')
                    with self.assertRaisesRegex(ValueError, 'contracts-assembly'): published_producer_contract(archive)
                    write(manifests + b' ', contracts)
                    with self.assertRaisesRegex(ValueError, 'supplier-assembly'): published_producer_contract(archive)

        def test_initial_commit_covers_every_complete_only_blob_and_mode(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); observed = boundary(root, a, ROWS[0])
                (root / 'Client/Api.fs').write_bytes(b'large blob\n' * 5000)
                runner = Mock()
                def git(r, *args):
                    if args[0] == 'rev-list': return ''
                    if args[0] == 'ls-files': return '\n'.join(q.relative_to(root).as_posix() for q in root.rglob('*') if q.is_file() and 'cache' not in q.parts)
                    if args[0] == 'rev-parse': return 'sha1'
                    name = args[-1]; raw = (root / name).read_bytes(); mode = '100755' if (root / name).stat().st_mode & 0o111 else '100644'
                    return mode + ' blob ' + hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest() + '\t' + name
                runner.git.side_effect = git
                with patch.object(M, 'ensure_git'), patch.object(M, 'commit', return_value='head'):
                    result = complete_initial(runner, root, a, ROWS[0], observed)
                    for name in COMPLETE_FILES: self.assertIn(name, result['gitBlobs'])
                    self.assertEqual(result['gitBlobs']['deploy/run-local.sh']['mode'], '100755')
                    self.assertIn('.codex/skills/owner/SKILL.md', result['gitBlobs'])
                    with self.assertRaisesRegex(ValueError, 'complete-only-initial-blob-coverage'):
                        complete_initial(runner, root, a, ROWS[0], dict(observed, completeOnlyFiles=observed['completeOnlyFiles'] + ['Missing/Complete.fs']))
                    runner.git.side_effect = lambda r, *args: ('100644 blob wrong\t' + args[-1]) if args[0] == 'ls-tree' and args[-1] == 'SvgFoundation/Examples/FourD/reference.json' else git(r, *args)
                    with self.assertRaisesRegex(ValueError, 'initial-owner-blob'): complete_initial(runner, root, a, ROWS[0], observed)

        def test_published_initial_bytes_modes_and_seed_are_causal(self):
            with tempfile.TemporaryDirectory() as td:
                root, a, i, p = self.build(td); boundary(root, a, ROWS[0])
                for name in INITIAL_HASHES:
                    target = root / name; raw = target.read_bytes(); target.write_bytes(b'nonempty corrupt lifecycle')
                    with self.assertRaisesRegex(ValueError, 'published-initial-file'): boundary(root, a, ROWS[0])
                    target.write_bytes(raw); target.chmod(0o600)
                    with self.assertRaisesRegex(ValueError, 'published-initial-file'): boundary(root, a, ROWS[0])
                    target.chmod(0o644)
                target = root / '.fsgg/knowledge/records/project-knowledge.json'; target.chmod(0o600)
                with self.assertRaisesRegex(ValueError, 'published-initial-seed-mode'): boundary(root, a, ROWS[0])
                target.chmod(0o644); seed = json.loads(target.read_bytes()); seed['Summary'] = 'plausible but wrong'; target.write_text(json.dumps(seed))
                with self.assertRaisesRegex(ValueError, 'published-initial-seed'): boundary(root, a, ROWS[0])

        def test_ports_and_producer_root_contract_are_sealed(self):
            self.assertEqual(AGENT_ROOTS, ('.claude', '.agents'))
            with patch(__name__ + '.PLAYER_PROVIDER_SHA', '0' * 64):
                with self.assertRaisesRegex(ValueError, 'accepted-player-provider-ports-drift'): load_ports()
            with self.assertRaisesRegex(ValueError, 'schemas-source'): authenticated_agent_roots(PL.ROOT_DECLARATION)

        def test_probe_setup_commit_must_differ_from_caller_initial(self):
            with tempfile.TemporaryDirectory() as td:
                archive = Path(td) / 'archive'; archive.write_bytes(b'public'); descriptor = Path(td) / 'descriptor'; descriptor.write_bytes(b'descriptor'); base = Path(td) / 'fresh'
                m = {'workRoot': str(base), 'report': str(base / 'report.json'), 'sddExecutable': '/sdd', 'dotnetExecutable': '/dotnet',
                     'providerDescriptor': str(descriptor), 'packages': {k: {'archive': str(archive)} for k in PACKAGES}}
                runner = Mock(); runner.sequence = 0; runner.call.return_value.stdout = '2.2.0\n'
                original_read = Path.read_bytes
                with patch(__name__ + '.validate', return_value=m), patch.object(P, 'injected_runner', return_value=runner), \
                        patch.object(M, 'env_for', return_value={}), patch.object(C, 'registration', return_value={}), \
                        patch(__name__ + '.boundary', return_value={'productFiles': []}), patch.object(M, 'snapshot', return_value={}), \
                        patch.object(Path, 'read_bytes', autospec=True, side_effect=lambda q: b'ignore' if q.name == '.gitignore' else original_read(q)), \
                        patch(__name__ + '.complete_initial', return_value={'commit': 'same'}), patch(__name__ + '.restore_tools'), \
                        patch.object(P, 'knowledge', return_value={'probe': {'probeSetupCommit': 'same'}}):
                    with self.assertRaisesRegex(ValueError, 'probe-not-caller-initial'): qualify('/sealed', 'sha', runner_factory=lambda: None)

        def test_actual_driver_two_rows_caller_commit_precedes_probe_with_bundle(self):
            with tempfile.TemporaryDirectory() as td:
                archive = Path(td) / 'archive'; archive.write_bytes(b'public'); descriptor = Path(td) / 'descriptor'; descriptor.write_bytes(b'descriptor'); base = Path(td) / 'fresh'
                m = {'workRoot': str(base), 'report': str(base / 'report.json'), 'sddExecutable': '/sdd', 'dotnetExecutable': '/dotnet',
                     'providerDescriptor': str(descriptor), 'packages': {k: {'archive': str(archive)} for k in PACKAGES}}
                runner = Mock(); runner.sequence = 0; runner.call.return_value.stdout = '2.2.0\n'; events = []
                def commit(*args): events.append('initial'); return {'commit': 'caller'}
                def probe(*args): self.assertEqual(events[-1], 'initial'); events.append('probe'); return {'probe': {'probeSetupCommit': 'later'}}
                original_read = Path.read_bytes
                with patch(__name__ + '.validate', return_value=m), patch.object(P, 'injected_runner', return_value=runner), \
                        patch.object(M, 'env_for', return_value={}), patch.object(C, 'registration', return_value={}), \
                        patch(__name__ + '.boundary', return_value={'productFiles': []}), patch.object(M, 'snapshot', return_value={}), \
                        patch.object(Path, 'read_bytes', autospec=True, side_effect=lambda q: b'ignore' if q.name == '.gitignore' else original_read(q)), \
                        patch(__name__ + '.complete_initial', side_effect=commit), patch(__name__ + '.restore_tools'), \
                        patch.object(P, 'knowledge', side_effect=probe), patch.object(P, 'preserved'), patch(__name__ + '.hybrid_manifest'), \
                        patch.object(P, 'initialize', side_effect=AssertionError('standalone forbidden')):
                    result = qualify('/sealed', 'sha', runner_factory=lambda: None)
                self.assertEqual(events, ['initial', 'probe', 'initial', 'probe']); self.assertEqual(result['state'], 'passed')
                creates = [c.args[0] for c in runner.call.call_args_list if c.args[0][1] == 'scaffold']
                self.assertEqual(len(creates), 2); self.assertNotIn('lifecycle=typed-sdd', creates[1])
                self.assertTrue(all('bundle=complete' in args for args in creates))
                self.assertFalse(any(str(a).startswith('svgFoundation=') for args in creates for a in args))

        def test_closed_schema_and_factory_fail_before_runtime(self):
            with self.assertRaisesRegex(ValueError, 'qualified-runner-factory-required'): qualify('/missing', 'sha')
            with tempfile.TemporaryDirectory() as td:
                p = Path(td) / 'manifest'; p.write_text(json.dumps(dict.fromkeys(TOP_KEYS)))
                with self.assertRaisesRegex(ValueError, 'closed-provider-manifest'): validate(p, digest(p))
                # The PLAYER provider schema is not accepted by the COMPLETE adapter.
                value = dict.fromkeys(TOP_KEYS); value['schema'] = 'fsgg.provider-fable-player-knowledge/1'; p.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError, 'closed-provider-manifest'): validate(p, digest(p))

    class RealPublicPackage(unittest.TestCase):
        # Opt-in against the published Workspace.Template 0.19.0 archive; hosted
        # CI has no package, so this skips there and never fetches anything itself.
        @unittest.skipUnless(os.environ.get('FSGG_COMPLETE_PROVIDER_WORKSPACE_PACKAGE'), 'published package not supplied')
        def test_published_package_has_exact_171_inventory_12_exclusions_and_53_complete_only(self):
            archive = Path(os.environ['FSGG_COMPLETE_PROVIDER_WORKSPACE_PACKAGE'])
            self.assertEqual(digest(archive), PACKAGES['workspace'][3])
            for row in ROWS:
                inventory, excluded = product_inventory(archive, row)
                self.assertEqual((len(inventory), len(excluded)), (APPLICABLE_FILE_COUNT, 12))
                self.assertTrue(all(inventory[n][1] == 0o755 for n in inventory if n.endswith('.sh')))
            self.assertEqual(len(complete_only(archive)), COMPLETE_ONLY_COUNT)

    class RealPublicProducer(unittest.TestCase):
        # Opt-in against the published FS.GG.SDD.Cli 2.2.0 archive's compiled manifests.
        @unittest.skipUnless(os.environ.get('FSGG_COMPLETE_PROVIDER_PRODUCER_PACKAGE'), 'published producer package not supplied')
        def test_published_commands_assembly_yields_exactly_the_five_complete_suppliers(self):
            archive = Path(os.environ['FSGG_COMPLETE_PROVIDER_PRODUCER_PACKAGE'])
            self.assertEqual(digest(archive), PACKAGES['producer'][3])
            with zipfile.ZipFile(archive) as z:
                raw = z.read('tools/net10.0/any/FS.GG.SDD.Commands.dll')
            self.assertEqual(authenticate_complete_suppliers(raw), sorted(COMPLETE_SUPPLIERS))

    with patch.object(C.subprocess, 'Popen', side_effect=AssertionError('complete-provider-source-controls-launched-child')):
        suite = unittest.TestSuite([unittest.defaultTestLoader.loadTestsFromTestCase(Controls),
                                    unittest.defaultTestLoader.loadTestsFromTestCase(RealPublicPackage),
                                    unittest.defaultTestLoader.loadTestsFromTestCase(RealPublicProducer)])
        result = unittest.TextTestRunner(verbosity=2).run(suite)
    require(result.wasSuccessful(), 'complete-provider-source-controls-failed')


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--source-controls', action='store_true'); args = parser.parse_args()
    require(args.source_controls, 'root-qualified-runner-factory-and-runtime-admission-required'); source_controls()


if __name__ == '__main__': main()
