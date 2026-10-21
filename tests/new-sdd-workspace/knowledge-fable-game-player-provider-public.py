#!/usr/bin/env python3
"""Published019 PLAYER provider: explicit and genuinely omitted typed lifecycle.

Pure controls launch no children. Runtime needs separate root admission, sealed
public inputs and the accepted finite runner. Fixed ASCII parameters only.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
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

PROVIDER_SHA = '30a288f18e32da8e22ee6a226df56af6f4bace448ab96620c19f7103eeeb18f7'
COMMANDS_SHA = '74ece24ea1593ef16648e4d50b7c25c2d83564db56c46d2440589c89a8e78f33'
ROWS = ({'id': 'provider-fable-player-explicit-typed', 'product': 'ProviderPlayerTyped', 'explicit': True},
        {'id': 'provider-fable-player-omitted-typed', 'product': 'ProviderPlayerOmitted', 'explicit': False})


def require(ok, reason):
    if not ok: raise ValueError(reason)


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_ports():
    path = Path(__file__).with_name('knowledge-fable-game-provider-public.py').resolve()
    require(digest(path) == PROVIDER_SHA, 'accepted-provider-ports-drift')
    module = types.ModuleType('sealed_player_provider_ports'); module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


V = load_ports()
L, P, M, C = V.L, V.P, V.M, V.C
PACKAGES, TOP_KEYS, PINS = V.PACKAGES, V.TOP_KEYS, V.PINS
LIFECYCLE_FILES, LIFECYCLE_DIRS = V.LIFECYCLE_FILES, V.LIFECYCLE_DIRS
IGNORE_PREFIX, IGNORE_SUFFIX = V.IGNORE_PREFIX, V.IGNORE_SUFFIX
hybrid_manifest, rendered_owner, matches_glob = V.hybrid_manifest, V.rendered_owner, V.matches_glob
initial, restore_tools, descriptor_contract = V.initial, V.restore_tools, V.descriptor_contract
AGENT_ROOTS = ('.agents', '.claude', '.codex')
MANIFEST = '.agents/skills/skill-manifest.json'
PREFIX = P.PREFIX
# Fixed published2.2.0 compiled transport, independently extracted without CLR.
# The producer archive's Commands.dll is pinned below. These metadata/file hashes
# derive from Driver/GameSkill/RenderingSkill/AudioSkill.manifest and .skill/*.
# cf2f046a HandlersScaffold.productManifestAdditions defines order/predicates;
# profile is absent, template=fable-game, bundle=player. Profile-only rows do not
# materialize; driver/game union rows use always and omit supplied-by.
SUPPLIERS = {'work-roadmap': {'channel': 'Driver',
                  'sourcePredicate': 'always',
                  'sourceSupplier': '.claude/skills/work-roadmap',
                  'row': {'id': 'work-roadmap',
                          'scope': 'driver',
                          'sha256': 'b93acaf0145ab81ac5b6a888fd0fbfdb6205f3becc83e9cdd9d55de52b33419d',
                          'resolvablePath': '.agents/skills/work-roadmap/SKILL.md',
                          'materializes-when': 'always'},
                  'files': {'SKILL.md': 'b93acaf0145ab81ac5b6a888fd0fbfdb6205f3becc83e9cdd9d55de52b33419d',
                            'agents/openai.yaml': 'd15ec5205fcd2d3698bcfb5b82237036ed115c1a28bf4acd03aa4a8dd55def22',
                            'references/critique-contract.md': 'a5a6268aba163e1a735370de3114105eee9cc67dddfa4f41431c48ba884545d4',
                            'references/deep-detail.md': '8ebce8de2de77b8eb75de68eafe4dc68fd202557df1b0e47aff182c4c3dafa82',
                            'references/feedback-contract.md': '7342aa0c2c86d2f157b360741426f2769d901c67e1f6ef4549ddbd83f12937f1',
                            'references/host-loop.md': '4307c49c060eb20e864cd982a39fcecdad91763acfe4218a3259e3fb783a448e',
                            'references/lifecycle-log.md': '86452edc1985dd73b77ce06c1623306100ddc7fd415127d7b2394f7d32103700',
                            'references/roadmap-ledger.md': 'fd7b535511327319467da7c4082afdf4b5bd7ca46c258a7129eba14c3fe6c679'}},
 'work-board': {'channel': 'Driver',
                'sourcePredicate': 'always',
                'sourceSupplier': '.claude/skills/work-board',
                'row': {'id': 'work-board',
                        'scope': 'driver',
                        'sha256': '6717f8af6fc64cad57bdb10cb54a4c68cb1199163d9c8718dfee5996d6e5ca31',
                        'resolvablePath': '.agents/skills/work-board/SKILL.md',
                        'materializes-when': 'always'},
                'files': {'SKILL.md': '6717f8af6fc64cad57bdb10cb54a4c68cb1199163d9c8718dfee5996d6e5ca31',
                          'agents/openai.yaml': 'a4d2b19cde3fd490c669da9a007b16620a106586efc30c32a02efd92f58d0433',
                          'references/backlog-triage.md': '2fc359707e81ff48a197931d0baae4c2fb8e6af036954024641198bd6407c354',
                          'references/deep-detail.md': '87aae5dbb5834e6ecf98c3a656eddca573f8fe5af35c8adf77323a4e03981893',
                          'references/feedback-contract.md': '902d8d2ded50626492e6254a80c5ba0bb080bd47c3d23795ec5700982ee1bd41',
                          'references/host-loop.md': '88c5d8152eb7861b01806954539c175b69f853796f5bacfa49aabe2279cbbea3',
                          'references/workspace-scope.md': '5bc29dac67ee44b493ebcd88e1b225fc14198a90316e8068d34e320b319d2255'}},
 'work-board-normal': {'channel': 'Driver',
                       'sourcePredicate': 'always',
                       'sourceSupplier': '.claude/skills/work-board-normal',
                       'row': {'id': 'work-board-normal',
                               'scope': 'driver',
                               'sha256': '55f96951dc04fd02cdc3411dd72ce0f6fcaca9c64a2daafd6270fc0d45adf05b',
                               'resolvablePath': '.agents/skills/work-board-normal/SKILL.md',
                               'materializes-when': 'always'},
                       'files': {'SKILL.md': '55f96951dc04fd02cdc3411dd72ce0f6fcaca9c64a2daafd6270fc0d45adf05b',
                                 'agents/openai.yaml': '2058fafb85ceaf1b6bed448a4cb4f6b51bfdbee05c9eb062af0647cb27dc1ec0'}},
 'work-board-best': {'channel': 'Driver',
                     'sourcePredicate': 'always',
                     'sourceSupplier': '.claude/skills/work-board-best',
                     'row': {'id': 'work-board-best',
                             'scope': 'driver',
                             'sha256': '4b9c0fd59cfd68a16f7acc0acef726b10bb2903380af5cfd6cc2b0e5d7376868',
                             'resolvablePath': '.agents/skills/work-board-best/SKILL.md',
                             'materializes-when': 'always'},
                     'files': {'SKILL.md': '4b9c0fd59cfd68a16f7acc0acef726b10bb2903380af5cfd6cc2b0e5d7376868',
                               'agents/openai.yaml': '9d85ffa0636c28f3d4a6713217640b3572bb003ec5759ac5a00a3851a10ff548'}},
 'padd-item': {'channel': 'Driver',
               'sourcePredicate': 'always',
               'sourceSupplier': '.claude/skills/padd-item',
               'row': {'id': 'padd-item',
                       'scope': 'driver',
                       'sha256': '74025b26c92363fceca7a57aea3b295bfe2d47df74492717249e9715d9ca15ef',
                       'resolvablePath': '.agents/skills/padd-item/SKILL.md',
                       'materializes-when': 'always'},
               'files': {'SKILL.md': '74025b26c92363fceca7a57aea3b295bfe2d47df74492717249e9715d9ca15ef',
                         'agents/openai.yaml': 'b5e453d33a40ed8aa0a14a34b01b01fbe72f351d6269208a77e883b7efac02f8'}},
 'fs-gg-collision': {'channel': 'GameSkill',
                     'sourcePredicate': 'profile in [game, sample-pack] or template == fable-game',
                     'sourceSupplier': 'template/product-skills/fs-gg-collision/',
                     'row': {'id': 'fs-gg-collision',
                             'scope': 'product',
                             'sha256': '8bd79af6dfe1a0f8abbc57cf6d0a3a81bb5a0bbe6aa3e6f36f82a2e770e9b57f',
                             'resolvablePath': '.agents/skills/fs-gg-collision/SKILL.md',
                             'materializes-when': 'always'},
                     'files': {'SKILL.md': '8bd79af6dfe1a0f8abbc57cf6d0a3a81bb5a0bbe6aa3e6f36f82a2e770e9b57f'}},
 'fs-gg-game-core': {'channel': 'GameSkill',
                     'sourcePredicate': 'profile in [game, sample-pack] or template == fable-game',
                     'sourceSupplier': 'template/product-skills/fs-gg-game-core/',
                     'row': {'id': 'fs-gg-game-core',
                             'scope': 'product',
                             'sha256': '712f21c2ce2fa7866e7696463ae8ebb8b3a4afd2ddc73624c73514e3bd5d8e77',
                             'resolvablePath': '.agents/skills/fs-gg-game-core/SKILL.md',
                             'materializes-when': 'always'},
                     'files': {'SKILL.md': '712f21c2ce2fa7866e7696463ae8ebb8b3a4afd2ddc73624c73514e3bd5d8e77'}},
 'fs-gg-game-fable': {'channel': 'GameSkill',
                      'sourcePredicate': 'profile in [game, sample-pack] or template == fable-game',
                      'sourceSupplier': 'template/product-skills/fs-gg-game-fable/',
                      'row': {'id': 'fs-gg-game-fable',
                              'scope': 'product',
                              'sha256': '443a82d24a0b4bbd21f4499b06f6e3d12b95a36a858f3880b414b74cae1a5c50',
                              'resolvablePath': '.agents/skills/fs-gg-game-fable/SKILL.md',
                              'materializes-when': 'always'},
                      'files': {'SKILL.md': '443a82d24a0b4bbd21f4499b06f6e3d12b95a36a858f3880b414b74cae1a5c50'}},
 'fs-gg-persistence': {'channel': 'GameSkill',
                       'sourcePredicate': 'profile in [game, sample-pack] or template == fable-game',
                       'sourceSupplier': 'template/product-skills/fs-gg-persistence/',
                       'row': {'id': 'fs-gg-persistence',
                               'scope': 'product',
                               'sha256': '9461822e5c282f0cac37aa0a88720fbb910319a6571d74bbcb1119eeb48530fb',
                               'resolvablePath': '.agents/skills/fs-gg-persistence/SKILL.md',
                               'materializes-when': 'always'},
                       'files': {'SKILL.md': '9461822e5c282f0cac37aa0a88720fbb910319a6571d74bbcb1119eeb48530fb'}},
 'fs-gg-replay': {'channel': 'GameSkill',
                  'sourcePredicate': 'template == fable-game',
                  'sourceSupplier': 'template/product-skills/fs-gg-replay/',
                  'row': {'id': 'fs-gg-replay',
                          'scope': 'product',
                          'sha256': '51a45640aa9ef2331186f526fc30e4a2627920ed27dd687c02266d0146982048',
                          'resolvablePath': '.agents/skills/fs-gg-replay/SKILL.md',
                          'materializes-when': 'always'},
                  'files': {'SKILL.md': '51a45640aa9ef2331186f526fc30e4a2627920ed27dd687c02266d0146982048'}},
 'fs-gg-feedback-report': {'channel': 'RenderingSkill',
                           'sourcePredicate': 'always',
                           'sourceSupplier': 'template/feedback-report/skill/',
                           'row': {'id': 'fs-gg-feedback-report',
                                   'scope': 'product',
                                   'sha256': '1b6888de4b8ce96f61e7a98c2bb9e0b249cdca2cd90da7bec6884bdccf055fe2',
                                   'resolvablePath': '.agents/skills/fs-gg-feedback-report/SKILL.md',
                                   'materializes-when': 'always',
                                   'supplied-by': 'template/feedback-report/skill/'},
                           'files': {'SKILL.md': '1b6888de4b8ce96f61e7a98c2bb9e0b249cdca2cd90da7bec6884bdccf055fe2',
                                     'scripts/FeedbackReportTool.fs': 'a2c60784a5821fcccad3c1d0961b85369266de2a37db4a08e9fa15b01fd0a3e4',
                                     'scripts/feedback-tool.fsx': '28b41c0ba94d581184a47b6560561c08b097abcaae248b9f72e6145954028929'}},
 'fs-gg-game-shell': {'channel': 'RenderingSkill',
                      'sourcePredicate': 'profile in [app, game] or template == fable-game',
                      'sourceSupplier': 'template/product-skills/fs-gg-game-shell/',
                      'row': {'id': 'fs-gg-game-shell',
                              'scope': 'product',
                              'sha256': 'f96f9d61176f94265a19d815194bcad38eda4447bf3714add88edbb07d88521b',
                              'resolvablePath': '.agents/skills/fs-gg-game-shell/SKILL.md',
                              'materializes-when': 'profile in [app, game] or template == fable-game',
                              'supplied-by': 'template/product-skills/fs-gg-game-shell/'},
                      'files': {'SKILL.md': 'f96f9d61176f94265a19d815194bcad38eda4447bf3714add88edbb07d88521b'}},
 'fs-gg-keyboard-input': {'channel': 'RenderingSkill',
                          'sourcePredicate': 'profile in [app, game] or template == fable-game',
                          'sourceSupplier': 'template/product-skills/fs-gg-keyboard-input/',
                          'row': {'id': 'fs-gg-keyboard-input',
                                  'scope': 'product',
                                  'sha256': '950678226adbfc6456585adc77dfcf6f170b933e957d9582f3c450f2d06815ec',
                                  'resolvablePath': '.agents/skills/fs-gg-keyboard-input/SKILL.md',
                                  'materializes-when': 'profile in [app, game] or template == fable-game',
                                  'supplied-by': 'template/product-skills/fs-gg-keyboard-input/'},
                          'files': {'SKILL.md': '950678226adbfc6456585adc77dfcf6f170b933e957d9582f3c450f2d06815ec'}},
 'fs-gg-scene': {'channel': 'RenderingSkill',
                 'sourcePredicate': 'profile in [app, headless-scene, governed, sample-pack, game] or '
                                    'template == fable-game',
                 'sourceSupplier': 'template/product-skills/fs-gg-scene/',
                 'row': {'id': 'fs-gg-scene',
                         'scope': 'product',
                         'sha256': 'a058c26b4503eca677ca338d2d9a010442138445712eb6f801206059f7ec61f3',
                         'resolvablePath': '.agents/skills/fs-gg-scene/SKILL.md',
                         'materializes-when': 'profile in [app, headless-scene, governed, sample-pack, game] '
                                              'or template == fable-game',
                         'supplied-by': 'template/product-skills/fs-gg-scene/'},
                 'files': {'SKILL.md': 'a058c26b4503eca677ca338d2d9a010442138445712eb6f801206059f7ec61f3'}},
 'fs-gg-svg-performance': {'channel': 'RenderingSkill',
                           'sourcePredicate': 'template == fable-game',
                           'sourceSupplier': 'template/product-skills/fs-gg-svg-performance/',
                           'row': {'id': 'fs-gg-svg-performance',
                                   'scope': 'product',
                                   'sha256': '9cd65f7b1b24053717a5ef4e0b3c36b866e559cb3f32e79a383173ba34f06536',
                                   'resolvablePath': '.agents/skills/fs-gg-svg-performance/SKILL.md',
                                   'materializes-when': 'template == fable-game',
                                   'supplied-by': 'template/product-skills/fs-gg-svg-performance/'},
                           'files': {'SKILL.md': '9cd65f7b1b24053717a5ef4e0b3c36b866e559cb3f32e79a383173ba34f06536'}},
 'fs-gg-testing': {'channel': 'RenderingSkill',
                   'sourcePredicate': 'profile in [app, headless-scene, governed, sample-pack, game] or '
                                      'template == fable-game',
                   'sourceSupplier': 'template/product-skills/fs-gg-testing/',
                   'row': {'id': 'fs-gg-testing',
                           'scope': 'product',
                           'sha256': '37bc28dcbb7454a489aeea925c17b006715630752797a2734fcef6685dd4f037',
                           'resolvablePath': '.agents/skills/fs-gg-testing/SKILL.md',
                           'materializes-when': 'profile in [app, headless-scene, governed, sample-pack, '
                                                'game] or template == fable-game',
                           'supplied-by': 'template/product-skills/fs-gg-testing/'},
                   'files': {'SKILL.md': '37bc28dcbb7454a489aeea925c17b006715630752797a2734fcef6685dd4f037'}},
 'fs-gg-browser-audio': {'channel': 'AudioSkill',
                         'sourcePredicate': 'template == fable-game',
                         'sourceSupplier': 'template/product-skills/fs-gg-browser-audio/',
                         'row': {'id': 'fs-gg-browser-audio',
                                 'scope': 'product',
                                 'sha256': '07d9a0277044162611cffb20089577565fcc0ef802ac71f0cbc668cf70c0818c',
                                 'resolvablePath': '.agents/skills/fs-gg-browser-audio/SKILL.md',
                                 'materializes-when': 'template == fable-game',
                                 'supplied-by': 'template/product-skills/fs-gg-browser-audio/'},
                         'files': {'SKILL.md': '07d9a0277044162611cffb20089577565fcc0ef802ac71f0cbc668cf70c0818c'}}}
SUPPLIER_MANIFEST_HASHES = {'Driver': 'bd2dfe4104494c4f7d8dbe55dbbc3d72f99fb99fd183b8567adf704435bc7a32', 'GameSkill': '1843439e0cf5d9b41c5025639e4bf0578e2037fb936b805a6d99f18de3ec58d8', 'RenderingSkill': 'b76d3756ce7733e1c0be570ff1928215b1a2bfbaaea7671512d88f5c9656add2', 'AudioSkill': '1d138562a632fa258d3718ecde44b1352f9588208322541d9983014f482cce2b'}


def command(m, row, root):
    require(row in ROWS, 'unknown-player-provider-subject')
    argv = [m['sddExecutable'], 'scaffold', '--root', root, '--provider', 'fable-game',
            '--no-update', '--text', '--param', 'productName=' + row['product']]
    return argv + (['--param', 'lifecycle=typed-sdd'] if row['explicit'] else [])


def product_inventory(archive, row):
    require(row in ROWS, 'unknown-player-provider-subject')
    P.packed_contract(archive)
    with zipfile.ZipFile(archive) as z:
        config = json.loads(z.read(PREFIX + '.template.config/template.json'))
        source, = config['sources']
        require(source['source'] == './' and source['target'] == './', 'player-product-source-mapping')
        selected = {'(portalExample != true)', '(bundle == "player")',
                    '((bundle != "tactical") && (bundle != "complete"))',
                    '((bundle != "arcade") && (bundle != "complete"))', '(bundle != "complete")', 'true'}
        require({m['condition'] for m in source['modifiers']} == selected | {'(lifecycle != "typed-sdd")'},
                'player-selected-modifier-contract')
        excludes = source['exclude'] + [p for m in source['modifiers'] if m['condition'] in selected for p in m['exclude']]
        inventory, excluded = {}, []
        members = [i for i in z.infolist() if i.filename.startswith(PREFIX) and not i.is_dir()]
        require(len({i.filename for i in members}) == len(members), 'player-duplicate-member')
        for info in members:
            name = info.filename[len(PREFIX):]
            require(not Path(name).is_absolute() and '..' not in Path(name).parts, 'player-package-path')
            target = rendered_owner(name.encode(), row['product']).decode()
            if any(matches_glob(name, pattern) for pattern in excludes):
                excluded.append(target); continue
            mode = (info.external_attr >> 16) & 0o777
            require(mode == 0o644, 'player-public-product-mode:' + name)
            require(target not in inventory, 'player-substitution-collision')
            inventory[target] = (rendered_owner(z.read(info), row['product']), mode | 0o111 if target.endswith('.sh') else mode)
        require(set(L.OWNER_FILES) | {MANIFEST, 'SvgFoundation/SvgFoundation.fsproj'} <= set(inventory), 'player-positive-inventory')
        return inventory, excluded


def skill_union(root, inventory, provenance):
    original = json.loads(inventory[MANIFEST][0], object_pairs_hook=C.unique)
    require(set(original) == {'schemaVersion', 'skills'} and type(original['schemaVersion']) is int
            and original['schemaVersion'] == 1, 'player-product-manifest-schema')
    rows = original['skills']
    require(isinstance(rows, list) and rows and all(isinstance(r, dict) and isinstance(r.get('id'), str) for r in rows), 'player-product-manifest-rows')
    ids = [r['id'] for r in rows]
    require(len(set(ids)) == len(ids), 'player-duplicate-owner-row')
    expected = {r['id']: r for r in rows}
    for id, supplier in SUPPLIERS.items():
        if id not in expected: expected[id] = supplier['row']
    paths = {}
    for agent in AGENT_ROOTS:
        manifest_path = agent + '/skills/skill-manifest.json'
        target = root / manifest_path
        value = C.read_json(target)
        require(set(value) == {'schemaVersion', 'skills'} and type(value['schemaVersion']) is int
                and value['schemaVersion'] == original['schemaVersion'], 'player-union-schema')
        actual = value['skills']
        require(isinstance(actual, list) and all(isinstance(r, dict) and isinstance(r.get('id'), str) for r in actual), 'player-union-rows')
        require(len(actual) == len(expected) and len({r['id'] for r in actual}) == len(actual)
                and V.exact_json(sorted(actual, key=lambda r:r['id'])) == V.exact_json(sorted(expected.values(), key=lambda r:r['id'])),
                'player-exact-supplier-union')
        require(stat.S_IMODE(target.stat().st_mode) == 0o644, 'player-union-manifest-mode')
        # Schema1 cannot declare file sets. Check complete supplier files here,
        # independently of the manifest/provenance's assertions about itself.
        declared = set()
        for id, row in expected.items():
            prefix = '.agents/skills/' + id + '/'
            if id in ids:
                files = {name[len(prefix):]: hashlib.sha256(raw).hexdigest()
                         for name,(raw,_) in inventory.items() if name.startswith(prefix)}
                if not files:
                    require(id == 'fable-bindings' and row['materializes-when'] == 'template in [fable-bindings]', 'player-missing-applicable-owner-skill')
                    continue
                require(files.get('SKILL.md') == row['sha256'], 'player-owner-skill-digest')
            else: files = SUPPLIERS[id]['files']
            for relative, sha in files.items():
                name = agent + '/skills/' + id + '/' + relative; path = root / name
                require(path.is_file() and not path.is_symlink() and digest(path) == sha
                        and stat.S_IMODE(path.stat().st_mode) == 0o644, 'player-supplier-file:' + name)
                paths[name] = sha; declared.add(name)
        actual_files = {p.relative_to(root).as_posix() for p in (root / agent / 'skills').rglob('*') if p.is_file()
                        and not p.relative_to(root / agent / 'skills').as_posix().startswith('fs-gg-sdd-')}
        require(actual_files == declared | {manifest_path}, 'player-dangling-or-unexpected-skill-files')
        paths[manifest_path] = digest(target)
        recorded = [p for key in ('producedPaths','mirroredPaths','driverPaths','gameSkillPaths','renderingSkillPaths')
                    for p in provenance.get(key,[]) if p['path'] == manifest_path]
        require(recorded and all(p.get('sha256') == paths[manifest_path] for p in recorded), 'player-amended-manifest-provenance')
    return paths


def boundary(root, archive, row):
    original = P.packed_contract(archive)
    manifest = hybrid_manifest(root, original)
    inventory, excluded = product_inventory(archive, row)
    require(all(not (root / name).exists() for name in excluded), 'player-excluded-product-file')
    M.assets(root, archive, PREFIX)
    provenance = C.read_json(root / '.fsgg/scaffold-provenance.json')
    pairs = provenance['effectiveParameters']
    require(len({p['key'] for p in pairs}) == len(pairs), 'player-effective-duplicates')
    effective = {p['key']:p['value'] for p in pairs}
    require(provenance['providerName'] == 'fable-game' and provenance['templateRef'] == 'fs-gg-fable-game'
            and effective['productName'] == row['product'] and effective['lifecycle'] == 'typed-sdd'
            and 'svgFoundation' not in effective and 'bundle' not in effective, 'player-effective-provider-selection')
    require(all(p['path'] != '.config/dotnet-tools.json' for p in provenance['sddOwnedPaths']), 'player-whole-tool-manifest-ownership')
    skills = skill_union(root, inventory, provenance)
    owner = {}
    for name,(raw,mode) in inventory.items():
        target=root/name
        require(target.is_file() and not target.is_symlink() and stat.S_IMODE(target.stat().st_mode)==mode, 'player-owner-file-mode:' + name)
        if name in ('.config/dotnet-tools.json', MANIFEST): continue
        expected = IGNORE_PREFIX + raw + IGNORE_SUFFIX if name == '.gitignore' else raw
        require(target.read_bytes() == expected, 'player-owner-bytes:' + name)
        owner[name]=hashlib.sha256(raw).hexdigest()
    for key in ('producedPaths','mirroredPaths','driverPaths','gameSkillPaths','renderingSkillPaths'):
        for item in provenance.get(key,[]):
            name=item['path']; require(not Path(name).is_absolute() and '..' not in Path(name).parts, 'player-provenance-path')
            path=root/name;require(path.is_file() and not path.is_symlink(), 'player-provenance-file:' + name)
            if item.get('sha256'): require(digest(path)==item['sha256'], 'player-provenance-digest:' + name)
            skills[name]=digest(path)
    canonical=sorted(p.relative_to(root).as_posix() for p in (root/'.fsgg/knowledge').rglob('*') if p.is_file())
    require('.fsgg/knowledge/schema.json' in canonical and any('/records/' in p for p in canonical)
            and (root/'.fsgg/knowledge-guide.md').stat().st_size > 0
            and all((root/name).is_file() and (root/name).stat().st_size>0 for name in LIFECYCLE_FILES)
            and all((root/name).is_dir() for name in LIFECYCLE_DIRS), 'player-automatic-typed-knowledge')
    return dict(effectiveParameters=effective, bundle='player', selectorPrecedence=100, svgFoundationParameterOmitted=True,
                lifecycleOmitted=not row['explicit'], automaticInitialization=True, standaloneInitializeInvoked=False,
                knowledgeReady=True, ownerAssets=owner, manifest=manifest, skills=skills,
                productFiles=sorted(inventory), excludedProductFiles=sorted(excluded),canonicalFiles=canonical,
                lifecycleFiles=list(LIFECYCLE_FILES), supplierManifestHashes=SUPPLIER_MANIFEST_HASHES,
                provenanceSha256=digest(root/'.fsgg/scaffold-provenance.json'))

def validate(path, expected_sha, after=False):
    require(expected_sha and digest(path) == expected_sha, 'manifest-digest')
    m = C.read_json(path)
    require(set(m) == TOP_KEYS and m['schema'] == 'fsgg.provider-fable-player-knowledge/1', 'closed-provider-manifest')
    require(set(m['packages']) == set(PACKAGES), 'closed-public-provider-packages')
    for name in m['inputs']: C.regular(name, m['inputs'])
    C.regular(m['runnerPort'], m['inputs'])
    for name in (__file__, V.__file__, L.__file__, P.__file__, M.__file__, C.__file__): C.regular(Path(name).resolve(), m['inputs'])
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
    with zipfile.ZipFile(m['packages']['producer']['archive']) as archive:
        require(hashlib.sha256(archive.read('tools/net10.0/any/FS.GG.SDD.Commands.dll')).hexdigest() == COMMANDS_SHA, 'player-published-supplier-assembly')
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
    report = dict(schema='fsgg.provider-fable-player-knowledge-result/1', state='started', manifestSha256=expected_sha,
                  outerSeconds=600, perCommandMaxSeconds=180, rows=[], runtimeAdmission='external-required',
                  unfulfilled=['independent full-result acceptance', 'other parent4642 routes and retained upgrades',
                               'product/browser/Wizard/publication acceptance'])
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
            entry['initial'] = initial(runner, root, m['packages']['workspace']['archive'], row, entry['provider']); save()
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
    class Controls(unittest.TestCase):
        def setUp(self):
            body=b'fixture supplier\n';sha=hashlib.sha256(body).hexdigest()
            self.suppliers={'fixture-driver':dict(channel='Driver',sourcePredicate='always',sourceSupplier='fixture',
                row={'id':'fixture-driver','scope':'driver','sha256':sha,'resolvablePath':'.agents/skills/fixture-driver/SKILL.md','materializes-when':'always'},files={'SKILL.md':sha,'references/control.md':sha})}
            self.body=body
            self.seal=patch.dict(SUPPLIERS,self.suppliers,clear=True);self.seal.start();self.addCleanup(self.seal.stop)
            packed=patch.object(P,'packed_contract');self.packed=packed.start();self.addCleanup(packed.stop)

        def fixture(self, td, row=ROWS[0]):
            root=Path(td)/'receiver';root.mkdir();archive=Path(td)/'fixture.nupkg'
            original={'version':1,'isRoot':True,'x-root':False,'tools':{
                'fable':{'version':'5.18.0','commands':['fable'],'unknown':[False,1]},
                'fantomas':{'version':'8.0.5','commands':['fantomas'],'rollForward':False}}}
            self.packed.return_value=original
            owner=b'owner skill\n';sha=hashlib.sha256(owner).hexdigest()
            rows=[{'id':'owner','scope':'product','sha256':sha,'resolvablePath':'.agents/skills/owner/SKILL.md','materializes-when':'template in [fable-game]','supplied-by':'template/owner/'},
                  {'id':'fable-bindings','scope':'product','sha256':'a'*64,'resolvablePath':'.agents/skills/fable-bindings/SKILL.md','materializes-when':'template in [fable-bindings]','supplied-by':'template/bindings/'}]
            conditions=['(portalExample != true)','(lifecycle != "typed-sdd")','(bundle == "player")','((bundle != "tactical") && (bundle != "complete"))','((bundle != "arcade") && (bundle != "complete"))','(bundle != "complete")','true']
            exclusions=['PortalExample/**',list(M.CI_FILES),'SvgFoundation/Studio/**','SvgFoundation/Examples/Tactical/**','SvgFoundation/Examples/Arcade/**','models/**','SvgFoundation/PreviewFont.fs']
            config={'sources':[{'source':'./','target':'./','exclude':['**/.template.config/**'],
                'modifiers':[{'condition':c,'exclude':e if isinstance(e,list) else [e]} for c,e in zip(conditions,exclusions)]}]}
            files={n:('owner '+n+' FableGameWorkspaceNamespace\n').encode() for n in (*L.OWNER_FILES,'SvgFoundation/SvgFoundation.fsproj','Client/Api.fs','Protocol/Http.fs','FableGameWorkspace.slnx','deploy/run-local.sh',*M.CI_FILES)}
            for name in M.CI_FILES: files[name]=b'central CI fixture\n'
            files.update({'.template.config/template.json':json.dumps(config).encode(),'.config/dotnet-tools.json':json.dumps(original).encode(),'.gitignore':b'owner-ignore\n',MANIFEST:json.dumps({'schemaVersion':1,'skills':rows}).encode(),'.agents/skills/owner/SKILL.md':owner,
                          'SvgFoundation/Studio/Editor.fs':b'excluded','models/rules.qnt':b'excluded'})
            with zipfile.ZipFile(archive,'w') as z:
                for name,raw in files.items():
                    info=zipfile.ZipInfo(PREFIX+name);info.external_attr=(stat.S_IFREG|0o644)<<16;z.writestr(info,raw)
            inventory,_=product_inventory(archive,row)
            for name,(raw,mode) in inventory.items():
                p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw);p.chmod(mode)
            (root/'.config/dotnet-tools.json').write_text(json.dumps(dict(original,tools=dict(original['tools'],**V.OWNED_TOOLS))))
            (root/'.gitignore').write_bytes(IGNORE_PREFIX+b'owner-ignore\n'+IGNORE_SUFFIX)
            allrows=rows+[s['row'] for s in SUPPLIERS.values()]
            prov={'providerName':'fable-game','templateRef':'fs-gg-fable-game','effectiveParameters':[{'key':k,'value':v} for k,v in {'productName':row['product'],'lifecycle':'typed-sdd'}.items()],'sddOwnedPaths':[],'producedPaths':[],'mirroredPaths':[]}
            for agent in AGENT_ROOTS:
                for id,supplier in SUPPLIERS.items():
                    for relative in supplier['files']:
                        p=root/agent/'skills'/id/relative;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(self.body)
                p=root/agent/'skills/owner/SKILL.md';p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(owner)
                p=root/agent/'skills/skill-manifest.json';p.write_text(json.dumps({'schemaVersion':1,'skills':allrows}))
                prov['producedPaths' if agent=='.agents' else 'mirroredPaths'].append({'path':p.relative_to(root).as_posix(),'sha256':digest(p)})
            for name in ('.fsgg/knowledge/schema.json','.fsgg/knowledge/records/seed.json','.fsgg/knowledge-guide.md','.fsgg/providers.yml',*LIFECYCLE_FILES):
                p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('fixture')
            for name in LIFECYCLE_DIRS:(root/name).mkdir()
            (root/'.fsgg/scaffold-provenance.json').write_text(json.dumps(prov))
            return root,archive,inventory,prov

        def test_genuine_parameter_omission_and_explicit_lifecycle(self):
            for row in ROWS:
                args=command({'sddExecutable':'/sdd'},row,Path('/fresh'))
                self.assertEqual(args[1],'scaffold');self.assertEqual(args.count('--text'),1)
                self.assertFalse(any(str(a).startswith(('bundle=','svgFoundation=')) for a in args))
                self.assertEqual('lifecycle=typed-sdd' in args,row['explicit'])
            with self.assertRaisesRegex(ValueError,'unknown-player'):command({},dict(ROWS[0],explicit=False),Path('/fresh'))

        def test_full_inventory_selected_modifiers_and_renames(self):
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td);self.assertIn(ROWS[0]['product']+'.slnx',i)
                self.assertIn('SvgFoundation/SvgFoundation.fsproj',i);self.assertNotIn('models/rules.qnt',i)
                self.assertNotIn('SvgFoundation/Studio/Editor.fs',i);self.assertIn(M.CI_FILES[0],i)
                boundary(root,a,ROWS[0])
                for name in ('Client/Api.fs','Protocol/Http.fs','SvgFoundation/SvgFoundation.fsproj'):
                    path=root/name;raw=path.read_bytes();path.write_bytes(raw[:-1]+b'!')
                    with self.assertRaisesRegex(ValueError,'owner-bytes'):boundary(root,a,ROWS[0])
                    path.write_bytes(raw)
                path=root/'models/rules.qnt';path.parent.mkdir();path.write_bytes(b'wrong')
                with self.assertRaisesRegex(ValueError,'excluded-product'):boundary(root,a,ROWS[0])

        def test_owner_modes_and_narrow_shell_promotion(self):
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td);boundary(root,a,ROWS[0])
                for name in ('Client/Api.fs','deploy/run-local.sh'):
                    path=root/name;mode=stat.S_IMODE(path.stat().st_mode);path.chmod(0o600)
                    with self.assertRaisesRegex(ValueError,'owner-file-mode'):boundary(root,a,ROWS[0])
                    path.chmod(mode)

        def test_exact_union_members_metadata_and_preserved_owner_rows(self):
            mutations=[lambda rows:rows.pop(),lambda rows:rows.append(dict(rows[-1])),
                       lambda rows:rows[0].__setitem__('scope','driver'),lambda rows:rows[-1].__setitem__('materializes-when','false'),
                       lambda rows:rows[-1].__setitem__('supplied-by','invented'),lambda rows:rows[-1].__setitem__('sha256','0'*64)]
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td);skill_union(root,i,p)
                target=root/'.claude/skills/skill-manifest.json';raw=target.read_bytes()
                for mutate in mutations:
                    value=json.loads(raw);mutate(value['skills']);target.write_text(json.dumps(value))
                    with self.assertRaisesRegex(ValueError,'exact-supplier-union'):skill_union(root,i,p)
                target.write_bytes(raw)
                value=json.loads(raw);value['schemaVersion']=2;target.write_text(json.dumps(value))
                with self.assertRaisesRegex(ValueError,'union-schema'):skill_union(root,i,p)

        def test_all_supplier_file_sets_digests_modes_and_no_dangling_files(self):
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td)
                for agent in AGENT_ROOTS:
                    for relative in ('SKILL.md','references/control.md'):
                        target=root/agent/'skills/fixture-driver'/relative;raw=target.read_bytes();target.unlink()
                        with self.assertRaisesRegex(ValueError,'supplier-file'):skill_union(root,i,p)
                        target.write_bytes(raw[:-1]+b'!')
                        with self.assertRaisesRegex(ValueError,'supplier-file'):skill_union(root,i,p)
                        target.write_bytes(raw);target.chmod(0o755)
                        with self.assertRaisesRegex(ValueError,'supplier-file'):skill_union(root,i,p)
                        target.chmod(0o644)
                target=root/'.agents/skills/fixture-driver/extra';target.write_text('dangling')
                with self.assertRaisesRegex(ValueError,'dangling-or-unexpected'):skill_union(root,i,p)

        def test_active_owner_missing_and_inactive_predicate_not_an_exemption(self):
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td)
                missing=dict(i);missing.pop('.agents/skills/owner/SKILL.md')
                with self.assertRaisesRegex(ValueError,'missing-applicable'):skill_union(root,missing,p)
                original=json.loads(i[MANIFEST][0]);original['skills'][1]['materializes-when']='always'
                changed=dict(i);changed[MANIFEST]=(json.dumps(original).encode(),0o644)
                # First align the emitted rows to isolate inactive-row predicate refusal.
                for agent in AGENT_ROOTS:
                    target=root/agent/'skills/skill-manifest.json';v=json.loads(target.read_bytes());v['skills'][1]['materializes-when']='always';target.write_text(json.dumps(v))
                with self.assertRaisesRegex(ValueError,'missing-applicable'):skill_union(root,changed,p)

        def test_manifest_provenance_is_post_union_and_every_root(self):
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td);skill_union(root,i,p)
                p['mirroredPaths'][0]['sha256']='0'*64
                with self.assertRaisesRegex(ValueError,'amended-manifest-provenance'):skill_union(root,i,p)

        def test_real_lifecycle_missing_and_effective_raw_substitution_refuse(self):
            for row in ROWS:
                with tempfile.TemporaryDirectory() as td:
                    root,a,i,p=self.fixture(td,row);result=boundary(root,a,row)
                    self.assertEqual(result['lifecycleOmitted'],not row['explicit']);self.assertTrue(result['automaticInitialization'])
                    for name in LIFECYCLE_FILES:
                        target=root/name;raw=target.read_bytes();target.unlink()
                        with self.assertRaises((ValueError,FileNotFoundError)):boundary(root,a,row)
                        target.write_bytes(raw)
                    p['effectiveParameters'].append({'key':'svgFoundation','value':'false'})
                    (root/'.fsgg/scaffold-provenance.json').write_text(json.dumps(p))
                    with self.assertRaisesRegex(ValueError,'effective-provider-selection'):boundary(root,a,row)

        def test_initial_commit_includes_supplier_sidecars_and_large_owner_blob(self):
            with tempfile.TemporaryDirectory() as td:
                root,a,i,p=self.fixture(td);observed=boundary(root,a,ROWS[0])
                target=root/'Client/Api.fs';target.write_bytes(b'large blob\n'*5000)
                runner=Mock()
                # Use a precise metadata instrument, including the executable shell.
                def git(r,*args):
                    if args[0]=='rev-list':return ''
                    if args[0]=='ls-files':return '\n'.join(p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file() and 'cache' not in p.parts)
                    if args[0]=='rev-parse':return 'sha1'
                    name=args[-1];raw=(root/name).read_bytes();mode='100755' if (root/name).stat().st_mode&0o111 else '100644'
                    return mode+' blob '+hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest()+'\t'+name
                runner.git.side_effect=git
                with patch.object(M,'ensure_git'),patch.object(M,'commit',return_value='head'):
                    result=initial(runner,root,a,ROWS[0],observed)
                    self.assertIn('.codex/skills/fixture-driver/references/control.md',result['gitBlobs'])
                    runner.git.side_effect=lambda r,*args:'100644 blob wrong\t'+args[-1] if args[0]=='ls-tree' else git(r,*args)
                    with self.assertRaisesRegex(ValueError,'initial-owner-blob'):initial(runner,root,a,ROWS[0],observed)

        def test_closed_schema_and_factory_fail_before_runtime(self):
            with self.assertRaisesRegex(ValueError,'qualified-runner-factory-required'):qualify('/missing','sha')
            with tempfile.TemporaryDirectory() as td:
                p=Path(td)/'manifest';p.write_text(json.dumps(dict.fromkeys(TOP_KEYS)))
                with self.assertRaisesRegex(ValueError,'closed-provider-manifest'):validate(p,digest(p))

    with patch.object(C.subprocess,'Popen',side_effect=AssertionError('player-source-controls-launched-child')):
        result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    require(result.wasSuccessful(),'player-provider-source-controls-failed')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source-controls',action='store_true');args=parser.parse_args()
    require(args.source_controls,'root-qualified-runner-factory-and-runtime-admission-required');source_controls()


if __name__=='__main__':main()
