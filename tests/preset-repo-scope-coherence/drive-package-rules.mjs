#!/usr/bin/env node
/**
 * Drive `default.json` through Renovate's REAL matcher and assert what it actually decides.
 *
 * .github#1798, epic #266 (checks that cannot fail). This is the leg the org did not have.
 *
 * WHY THIS EXISTS, AND WHY THE OTHER GATES ARE NOT IT
 *   `default.json` is the org-shared preset. Its `packageRules` decide, for every FS-GG repo, which
 *   files Renovate is allowed to propose a bump against — and the whole difficulty is that a WRONG
 *   answer is silent in both directions. A rule that disables too much proposes nothing, and a
 *   non-proposal is not an error: it appears nowhere, reds nothing, and shows up only when somebody
 *   eventually asks why a pin has not moved in a month (#1533, #1552). Three gates already read this
 *   file and NONE of them can see that:
 *
 *     * `renovate-config-validator` grades SHAPE. The exact configuration that zeroed FS.GG.Net's
 *       entire NuGet surface validates clean, and it does not resolve presets at all (FS.GG.Net#33).
 *     * `check-preset-repo-scope-coherence.py` grades whether a rule's `matchRepositories` equals the
 *       roster's receivers for the fabric the rule CLAIMS. It cannot see a rule whose fabric no longer
 *       delivers the file at all — which is exactly what #1615 created and what #1798 is.
 *     * `check-pin-coherence.py` grades that the surviving disables exist and that no `ignorePaths`
 *       entry reaches this repo's baseline. It reasons about the config; it never runs it.
 *
 *   All three are statements ABOUT the preset. This one is a statement about what Renovate DOES with
 *   it: extract a real manifest with the real nuget manager, push each dep through the real
 *   `applyPackageRules`, and assert the verdict. `default.json`'s own prose has claimed for two
 *   revisions that it was "driven end-to-end through renovate's real matcher" — driven by a human,
 *   once, and then written down. A measurement nobody re-runs is a comment.
 *
 * WHAT IT ASSERTS (and every one of these reds if it flips)
 *   1. The engine tool manifest is ENABLED in every `receives: coordination-kit` receiver — derived
 *      from `registry/repos.yml`, never a hand list, because the hand list is the thing that rots.
 *      This is ADR-0068's delivery path, and until #1798 it was switched off in all seven.
 *   2. `dist/dotnet/.config/dotnet-tools.json` is still MANAGED in `FS-GG/.github`. That is the pin's
 *      only entry point into the org (#660) and the file `engine-pin-coherence` grades. The hazard is
 *      not hypothetical: `ignorePaths: [".config/dotnet-tools.json"]` is a SUBSTRING test that also
 *      swallows `dist/dotnet/…`, so a plausible spelling of rule 1 un-manages the baseline.
 *   3. The surviving `.props` disable still BITES in a build-config receiver, and still does NOT bite
 *      in a repo that hand-authors those files (#1552). Without 3 this file could pass by having no
 *      opinion at all — a driver that says "enabled" about everything proves nothing about anything.
 *   4. A NEGATIVE CONTROL: re-inject the rule #1798 deleted and require assertion 1 to FLIP while
 *      assertion 2 stays managed. This is what makes 1 and 2 falsifiable rather than decorative, and
 *      it is the leg that fails if someone re-adds the rule by hand.
 *
 * IT GRADES THE PRESET AS AUTHORED, NOT AS RESOLVED, AND SAYS SO
 *   `resolveConfigPresets` needs the network and would fold a second failure mode into this red.
 *   Whether the RESOLVED form still carries these rules is a real and different question, and it is
 *   .github#1568's, still open. One red must not carry two meanings (#1538).
 *
 * Argv: none. Env: RENOVATE_DIR (the installed renovate package root), REPO_ROOT (default `.`).
 * Exit: 0 pass, 1 a finding, 3 NO VERDICT — "I could not look" is never "I looked, and it is fine".
 */
import fs from 'node:fs';
import path from 'node:path';

const REPO_ROOT = process.env.REPO_ROOT ?? '.';
const RENOVATE_DIR = process.env.RENOVATE_DIR;

let fails = 0;
const ok = (m) => console.log(`  ok   — ${m}`);
const bad = (m, d) => { fails++; console.log(`  FAIL — ${m}\n         ${d}`); };

/** A NO VERDICT: the harness could not put itself in a position to grade. Never exit 0 from here. */
function noVerdict(why) {
  console.error(`::error::drive-package-rules: NO VERDICT — ${why} This is NOT a passing preset: ` +
    `the driver could not complete its check, so nothing below it was measured (#266).`);
  process.exit(3);
}

if (!RENOVATE_DIR) {
  noVerdict('RENOVATE_DIR is unset, so there is no pinned renovate to drive.');
}

let applyPackageRules, extractPackageFile, nugetManager, GlobalConfig, renovateVersion;
try {
  ({ applyPackageRules } = await import(path.join(RENOVATE_DIR, 'dist/util/package-rules/index.js')));
  nugetManager = await import(path.join(RENOVATE_DIR, 'dist/modules/manager/nuget/index.js'));
  ({ extractPackageFile } = nugetManager);
  ({ GlobalConfig } = await import(path.join(RENOVATE_DIR, 'dist/config/global.js')));
  renovateVersion = JSON.parse(fs.readFileSync(path.join(RENOVATE_DIR, 'package.json'), 'utf8')).version;
} catch (e) {
  noVerdict(`the pinned renovate at ${RENOVATE_DIR} did not load: ${e.message}.`);
}
// The nuget extractor calls findUpLocal() looking for a NuGet.config; without a localDir it throws
// on a path join rather than returning "no registries configured".
GlobalConfig.set({ localDir: path.resolve(REPO_ROOT), cacheDir: path.resolve(REPO_ROOT) });

console.log(`renovate ${renovateVersion} — the pinned binary, not \`latest\` (#238).`);

// --- the roster half: WHO must be able to receive a bump, derived rather than listed --------------
//
// A regex over the owning file, exactly as `check-pin-coherence.py` reads `sync-build-config.sh`'s
// FILES and the kit's `kind: config` rows. A hand list here would be the #1552 defect rebuilt inside
// the check written to prevent it: the preset's repo list went stale because a human kept it, and a
// fixture that keeps its own copy goes stale the same way and takes the alarm with it.
const rosterSrc = fs.readFileSync(path.join(REPO_ROOT, 'registry/repos.yml'), 'utf8');
const receivers = [];
for (const line of rosterSrc.split('\n')) {
  const m = /^\s*-\s*\{\s*id:.*?\bfull:\s*([^,\s]+).*?\breceives:\s*\[([^\]]*)\]/.exec(line);
  if (!m) continue;
  if (m[2].split(',').map((s) => s.trim()).includes('coordination-kit')) receivers.push(m[1]);
}
if (receivers.length === 0) {
  noVerdict('registry/repos.yml yielded no `receives: coordination-kit` repo. An empty receiver set ' +
    'makes every assertion below vacuously true — the exact shape this driver exists to refuse.');
}
console.log(`coordination-kit receivers, from the roster: ${receivers.join(', ')}`);

// --- the manifest half: WHAT renovate extracts from the real bytes --------------------------------
const MANIFEST = 'dist/dotnet/.config/dotnet-tools.json';
const manifestBytes = fs.readFileSync(path.join(REPO_ROOT, MANIFEST), 'utf8');
const extracted = await extractPackageFile(manifestBytes, '.config/dotnet-tools.json', {});
const deps = extracted?.deps ?? [];
if (deps.length === 0) {
  noVerdict(`renovate's nuget manager extracted NO dependency from ${MANIFEST}. Every "is it enabled" ` +
    'question below is then asked about nothing at all.');
}
if (!deps.some((d) => d.depName === 'fs.gg.coord.cli')) {
  noVerdict(`renovate extracted ${deps.map((d) => d.depName).join(', ')} from ${MANIFEST} but not ` +
    'fs.gg.coord.cli — the pin ADR-0068 hands to Renovate is not in what Renovate reads.');
}
console.log(`extracted from ${MANIFEST}: ${deps.map((d) => `${d.depName} ${d.currentValue}`).join(', ')}`);

// The four SHIPPED managerFilePatterns. `check-pin-coherence.py` mirrors these in Python (it is an
// offline gate and cannot import npm), so a change here means that mirror must be re-derived. Asserted
// rather than assumed: this is the only place the two can be compared.
const SHIPPED_PATTERNS = ['/\\.(?:cs|fs|vb|sql)proj$/', '/\\.(?:props|targets)$/',
  '/(^|/)dotnet-tools\\.json$/', '/(^|/)global\\.json$/'];
const actualPatterns = nugetManager.defaultConfig?.managerFilePatterns ?? [];
if (JSON.stringify(actualPatterns) !== JSON.stringify(SHIPPED_PATTERNS)) {
  bad('the nuget manager\'s shipped managerFilePatterns are what this repo recorded',
    `renovate ${renovateVersion} ships ${JSON.stringify(actualPatterns)}; recorded here and mirrored ` +
    'in check-pin-coherence.py\'s _NUGET_MANAGER_FILE_RE: ' + JSON.stringify(SHIPPED_PATTERNS) +
    '. Re-derive that regex before bumping the pin.');
} else {
  ok(`the nuget manager still ships exactly the four recorded managerFilePatterns`);
}

// --- the driver ------------------------------------------------------------------------------------
const preset = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'default.json'), 'utf8'));

// INTERNAL CONTROL. If the preset carries no file-scoped disable at all, every "is it enabled?"
// below answers "yes" for reasons that have nothing to do with the rules — a green that means the
// file was empty. Refuse instead (#266, and #1568's measured note about `applyPackageRules`).
const fileScopedDisables = (preset.packageRules ?? [])
  .filter((r) => r && Array.isArray(r.matchFileNames) && r.enabled === false);
if (fileScopedDisables.length === 0) {
  noVerdict('default.json carries no `matchFileNames` + `enabled: false` packageRule at all. Every ' +
    'assertion below would then pass by there being nothing to apply.');
}

/** Renovate's verdict for one (repo, packageFile, dep), through the real matcher. */
async function verdict(rules, repository, packageFile, dep) {
  const res = await applyPackageRules({
    ...preset,
    packageRules: rules,
    repository,
    packageFile,
    manager: 'nuget',
    depName: dep.depName,
    packageName: dep.packageName ?? dep.depName,
    datasource: dep.datasource ?? 'nuget',
    currentValue: dep.currentValue,
    versioning: dep.versioning,
  }, 'datasource-merge');
  // `applyPackageRules` is ASYNC. An un-awaited call yields a Promise whose `.enabled` is `undefined`
  // — which reads exactly like "nothing disabled it" and turns this whole driver into a check that
  // cannot fail. Measured and recorded on .github#1568; awaited above.
  return (res.enabled === false || res.skipReason === 'package-rules') ? 'DISABLED' : 'MANAGED';
}

async function expect(rules, repository, packageFile, want, label) {
  const got = [];
  for (const dep of deps) got.push(`${dep.depName}=${await verdict(rules, repository, packageFile, dep)}`);
  const allWant = got.every((g) => g.endsWith(want));
  if (allWant) ok(`${label} — ${got.join(' ')}`);
  else bad(label, `wanted every dep ${want}; renovate ${renovateVersion} says ${got.join(' ')}`);
}

const RULES = preset.packageRules ?? [];

console.log('\n--- 1. ADR-0068\'s delivery path is LIVE in every kit receiver (#1798) ---');
for (const repo of receivers) {
  if (repo === 'FS-GG/.github') continue; // the authority receives no kit; it is the source.
  await expect(RULES, repo, '.config/dotnet-tools.json', 'MANAGED',
    `${repo} .config/dotnet-tools.json`);
}

console.log('\n--- 2. the baseline stays MANAGED here (#660, AC2) ---');
await expect(RULES, 'FS-GG/.github', MANIFEST, 'MANAGED', `FS-GG/.github ${MANIFEST}`);

console.log('\n--- 3. the surviving .props disable still bites, and still only where it should (#1552) ---');
await expect(RULES, 'FS-GG/FS.GG.SDD', 'Directory.Packages.props', 'DISABLED',
  'FS-GG/FS.GG.SDD Directory.Packages.props (a build-config receiver: materialized)');
await expect(RULES, 'FS-GG/FS.GG.Net', 'Directory.Packages.props', 'MANAGED',
  'FS-GG/FS.GG.Net Directory.Packages.props (hand-authored there)');

console.log('\n--- 4. NEGATIVE CONTROL: re-inject the rule #1798 deleted ---');
// Byte-for-byte the rule as it stood at babc650, so this leg measures the real thing rather than a
// paraphrase of it. If assertion 1 does not flip under it, assertion 1 was never testing anything.
const REINJECTED = [...RULES, {
  description: ['the rule #1798 deleted, re-injected as a negative control',
    'fsgg-repo-scope: receives=coordination-kit'],
  matchFileNames: ['.config/dotnet-tools.json'],
  matchRepositories: receivers.filter((r) => r !== 'FS-GG/.github'),
  enabled: false,
}];
for (const repo of receivers) {
  if (repo === 'FS-GG/.github') continue;
  await expect(REINJECTED, repo, '.config/dotnet-tools.json', 'DISABLED',
    `[control] ${repo} .config/dotnet-tools.json with the rule back`);
}
await expect(REINJECTED, 'FS-GG/.github', MANIFEST, 'MANAGED',
  `[control] the baseline survives the rule (matchFileNames anchors; ignorePaths would not)`);

// --- 5. the FS.GG.Kit automerge rule (#1587) ------------------------------------------------------
//
// WHY THIS LEG EXISTS. `#1587` automerges the MECHANICAL kit bump class and nothing else, with NO
// context armed anywhere: a red `materialize / kit-bump-mechanical` stops the merge because
// Renovate's own pre-merge poll reads the branch's COMBINED status. That only holds while RENOVATE
// does the merging. Renovate's default `platformAutomerge: true` instead hands the merge to GitHub's
// native auto-merge, which fires on the REQUIRED subset — and a context required NOWHERE is invisible
// to it, so the `mechanical + repair` class the owner reserved for a person would merge itself.
// `platformAutomerge: false` in the preset is the whole of what prevents that, and deleting it is
// SILENT: the config still validates, every other gate stays green, and the first symptom is a
// repair-carrying bump merged with nobody having read it (FS.GG.Rendering#1088). So the line gets a
// gate, and leg 5d makes that gate falsifiable rather than decorative.

/** The automerge decision Renovate reaches for one (repo, dep), through the real matcher. */
async function automergeVerdict(rules, repository, dep) {
  const res = await applyPackageRules({
    ...preset,
    packageRules: rules,
    repository,
    packageFile: 'Directory.Packages.props',
    manager: 'nuget',
    depName: dep,
    packageName: dep,
    datasource: 'nuget',
    currentValue: '0.18.0',
  }, 'datasource-merge');
  // Awaited, for the #1568 reason recorded on `verdict` above: an un-awaited Promise's `.automerge`
  // is `undefined`, which reads exactly like "automerge is off" and would make this leg unfailable.
  return { automerge: res.automerge, type: res.automergeType, platform: res.platformAutomerge };
}

async function expectAutomerge(rules, repository, dep, want, label) {
  const v = await automergeVerdict(rules, repository, dep);
  const got = `automerge=${v.automerge} automergeType=${v.type} platformAutomerge=${v.platform}`;
  const hit = want === 'ON'
    ? (v.automerge === true && v.type === 'pr' && v.platform === false)
    : v.automerge !== true;
  if (hit) ok(`${label} — ${got}`);
  else bad(label, `wanted automerge ${want}; renovate ${renovateVersion} says ${got}`);
}

console.log('\n--- 5. FS.GG.Kit automerges as `mechanical`-only, and nothing else does (#1587) ---');

// 5a. the kit itself, in a receiver: on, through a PR, with platform automerge OFF.
for (const repo of ['FS-GG/FS.GG.Net', 'FS-GG/FS.GG.Templates', 'FS-GG/FS.GG.SDD']) {
  await expectAutomerge(RULES, repo, 'FS.GG.Kit', 'ON', `${repo} FS.GG.Kit`);
}

// 5b. AC3 — the rule matches FS.GG.Kit ONLY. A blanket dependency automerge is out of scope, and the
// guard that makes automerge safe is derived from the KIT's materialize contract: it says nothing
// about any other package, so a wider match would automerge what nothing is checking.
for (const dep of ['FS.GG.Contracts', 'FS.GG.SDD.Cli', 'FS.GG.Kit.Extras', 'FSharp.Core']) {
  await expectAutomerge(RULES, 'FS-GG/FS.GG.Net', dep, 'OFF', `FS-GG/FS.GG.Net ${dep} (not the kit)`);
}

// 5c. the #660 casing trap, inverted: `matchPackageNames` is a PLAIN STRING here, so it routes to
// minimatch with `nocase: true` rather than to the case-SENSITIVE regex path. A re-cased pin must
// still automerge — otherwise the rule silently stops applying the day a pin is spelled differently.
await expectAutomerge(RULES, 'FS-GG/FS.GG.Net', 'fs.gg.kit', 'ON', 'FS-GG/FS.GG.Net fs.gg.kit (re-cased)');

// 5d. NEGATIVE CONTROL, and the reason legs 5a-5c are worth anything: drop `platformAutomerge: false`
// from the kit rule and require 5a to FLIP. If it does not, this whole section is measuring nothing
// and the load-bearing line could be deleted under a green board.
const NO_PLATFORM_OFF = RULES.map((r) => {
  if (!(r && Array.isArray(r.matchPackageNames) && r.matchPackageNames.includes('FS.GG.Kit'))) return r;
  const { platformAutomerge, ...rest } = r;
  return rest;
});
{
  const v = await automergeVerdict(NO_PLATFORM_OFF, 'FS-GG/FS.GG.Net', 'FS.GG.Kit');
  if (v.automerge === true && v.platform !== false) {
    ok(`[control] dropping platformAutomerge:false re-arms GitHub-native auto-merge — ` +
       `automerge=${v.automerge} platformAutomerge=${v.platform} (leg 5a can fail)`);
  } else {
    bad('[control] dropping platformAutomerge:false must flip leg 5a',
        `it did not: automerge=${v.automerge} platformAutomerge=${v.platform}. Leg 5a is therefore ` +
        `not testing the line it claims to, and the line could be deleted silently.`);
  }
}

console.log(`\n${fails === 0 ? 'all legs green' : `${fails} FAILED`}`);
process.exit(fails === 0 ? 0 : 1);
