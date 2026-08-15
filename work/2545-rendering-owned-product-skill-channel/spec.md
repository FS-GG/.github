---
schemaVersion: 1
workId: 2545-rendering-owned-product-skill-channel
title: Rendering Owned Product Skill Channel
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Rendering Owned Product Skill Channel Specification

Prose status: specified

## User Value

A maintainer adding a skill row to `registry/skills.yml` learns from a gate — rather than from a
scaffolded product tree months later — whether that row's class has a delivered byte channel at all.

ADR-0063 named the class: *"declared ∧ gated-in ∧ supplied-from-nowhere … the `owner`/`source` fields
name where the bytes are, and nothing reads them for delivery."* It has now been found three times, and
each time by a different accident:

| Instance | Row | How it surfaced | Closed |
|---|---|---|---|
| 1 (`.github#1299`) | `fs-gg-playtest` (`owner: fs-gg-game`) | a human noticed it absent from `Rougue1` | 2026-07-21 |
| 2 (`.github#1300`) | `workRoadmap` (`owner: .github`) | a consumer repo filed FS.GG.SDD#620 | 2026-07-21 |
| 3 (`.github#2380` → this row) | `fs-gg-feedback-report` (`owner: fs-gg-rendering`) | `EHotwagner/S.I.R.#193` could not run a tool the missing skill ships | open |

Nothing in the fabric *asks* the question. Instance 3 took two items and a full investigation package
(`work/2380-feedback-report-materialization/`) to establish, and the answer was structural and could
have been read off the two declarations `.github` already owns. The value delivered here is that the
question is asked mechanically, on every pull request and every night, so a fourth instance is a red
gate rather than a fourth investigation.

The route decision this item owes is recorded in `## Route decision` below; the mechanical arm is what
stops the decision from having to be rediscovered per class.

## Scope

- SB-001: Choose, on the record, the delivery route for the `scope: product, owner: fs-gg-rendering`
  skill class, with the ADR-0063 / ADR-0058 / ADR-0062 consistency argument stated
  (`.github#2545` acceptance criterion 1).
- SB-002: Add `registry/skills.delivery-channels.yml` — a `.github`-authored declaration binding every
  `(owner, scope)` skill class present in `registry/skills.yml` to exactly one accountable disposition
  (`delivered`, `provider-scoped`, `withheld`, `gap`) with that disposition's required fields.
- SB-003: Add a `delivery-channel` arm to `scripts/fsgg-skill-registry-check` that **derives** the class
  set from `registry/skills.yml` and reports a finding for any class with no entry, any entry matching no
  registry row, any duplicate entry, and any entry missing a field its disposition requires.
- SB-004: Ship the arm with an offline fixture in `tests/skill-registry/run.sh` and record
  gate-inversion evidence: the exact mutation applied and the observed red.
- SB-005: File the receiver rows the chosen route requires at the byte owner and at the consumer,
  deduped over REST against the cause, and record their numbers here
  (`.github#2545` acceptance criterion 2).
- SB-006: State, for every one of the 18 Rendering-owned product rows, whether it is in scope of the
  same channel or deliberately profile-gated out of non-rendering providers
  (`.github#2545` acceptance criterion 4).
- SB-007: Wire the new declaration into the workflow trigger filter that runs the check, so editing it
  cannot silently skip the gate that reads it.

## Non-Goals

- SB-101: Does **not** vendor any `FS.GG.Rendering` SKILL.md bytes into `.github`, and does not change
  `src/FS.GG.Drivers/`, `registry/driver-skill-manifest.json`, or `scripts/generate-driver-manifest`.
  That is Route A, and `## Route decision` refutes it. Those three paths remain in this item's declared
  `Paths:` because the filing declared them; they are deliberately untouched.
- SB-102: Does **not** publish `FS.GG.Rendering.Skills` or teach the `FS.GG.SDD` scaffold materializer
  to consume it. `.github` owns neither the bytes nor the materializer; those are the receiver rows
  SB-005 files, exactly as `.github#1308` routed the game class to FS.GG.Game#449.
- SB-103: Does **not** edit `registry/skills.yml`. No row's `id`, `scope`, `owner`, `source`, `sha256`,
  or `materializes-when` changes; the new declaration is a second, separate file so the
  `registry-schema` contract (owner `sdd`, consumer `github`) is untouched and no cross-repo validator
  sees a new key.
- SB-104: Does **not** re-open, amend, or supersede ADR-0063. This work applies ADR-0063's existing
  decision to a third class and makes the class boundary mechanical; it proposes no different decision.
- SB-105: Does **not** attempt to verify, offline, that a declared channel actually delivers bytes.
  The arm checks that an accountable disposition exists for every class; whether
  `FS.GG.Game.Skills` restores correctly is FS.GG.SDD's consumer gate, not this one.
- SB-106: Does **not** touch `EHotwagner/S.I.R.` or any product tree. That posture is `.github#2548`.
- SB-107: Does not implement Governance policy enforcement.

## Route decision

**Route B — a Rendering-owned, published, pinned, content-addressed package
(`FS.GG.Rendering.Skills`), consumed by the `FS.GG.SDD` scaffold materializer — is chosen. Route A is
refuted, not merely deprioritised.**

### Why Route A is refuted

Route A is *"extend `.github`'s byte-transport (`DELIVERED_SCOPES`) to stage Rendering-owned product
rows."* `.github`'s `FS.GG.Drivers` package stages from `REPO_ROOT/<supplied-by>/…`
(`src/FS.GG.Drivers/stage-drivers.py:136`), and `.github`'s package CI has no FS.GG.Rendering checkout.
For `.github` to pack Rendering's bytes it must hold a **copy** of them. That copy is:

- the **restatement** ADR-0058 forbids as the default (*"if a fact has an authoritative home, read it —
  never copy it"*);
- the byte-copy sync ADR-0062 **replaces** with a versioned package;
- and the frozen-donor trap ADR-0063 explicitly **rules out** (*"The frozen-donor route cannot close
  it … the FS.GG.Rendering#505 trap"*).

This is not a fresh argument. `.github#1308` asked for exactly this extension, for the game class, with
exactly this item's three declared paths, and was closed **superseded** on 2026-07-21 with no `.github`
change: *"`.github` is the wrong place … For `.github` to carry them it would need a frozen copy of
FS.GG.Game's bytes — the exact restatement ADR-0058 forbids."* Nothing distinguishes the Rendering
class from the game class on this axis. Choosing Route A here would be a decision to reverse #1308.

### Why Route B is chosen

ADR-0063's Decision already requires *"a delivered, pinned, content-addressed channel"* sourced from the
`owner`/`source` the registry row names. `.github#1300` picked the transport shape for every class:
**each owner publishes its own `FS.GG.Kit`-shaped package**. That shape has shipped twice and both
deliveries are measured working in the same tree:

- `.github` → `FS.GG.Drivers` (`.github#1304`/`#1306`) — 42 `driverPaths` in `EHotwagner/S.I.R.`;
- `FS.GG.Game` → `FS.GG.Game.Skills` (FS.GG.Game#449/PR#450) — `gameSkillPaths` in the same tree,
  recorded in `.github` as the `game-skills` contract at `registry/dependencies.yml`.

Route B is that shape a third time. It is the *smallest* consistent choice, not a new mechanism: no new
ADR, no new substrate, no new provider, and the `.github`-side artefact is the same one the game class
already has — a `registry/dependencies.yml` contract row recording owner, consumer, and pinned package
version, added once the package exists on the feed.

### Why a third site-by-site repair is not enough, and what this item adds

`.github#2545`'s own delivery-route rationale demands an answer to *"why closing it twice did not
generalise; a third site-by-site repair is the outcome to argue against explicitly rather than default
into."*

It did not generalise because **ADR-0063 enrolled classes by hand.** Its `Affects:` line names
FS.GG.Rendering only as the repo whose *frozen game copies are retired*; it never made Rendering a
delivery **source** for Rendering's own product rows, because the fabric assumed a restored rendering
provider template supplied them. Under a `fable-game` provider that assumption is vacuous, and nothing
anywhere noticed — the registry knows the row exists, knows its owner, knows its predicate, and no
artefact in the org states which channel carries it.

So the generalisation is not a fourth transport. It is: **make the channel an accountable, declared
fact per class, derived from the registry, and gate on its presence.** After this item, a new owner or
a new scope class appearing in `registry/skills.yml` reds the gate until someone writes down either the
channel that carries it or the issue that owes it. A fourth instance of ADR-0063's class can still be
*created* — but it cannot be created *silently*, which is the only property all three instances shared.

## The channel declaration and its disposition vocabulary

`registry/skills.delivery-channels.yml` carries one entry per `(owner, scope)` class present in
`registry/skills.yml`. Each entry declares **exactly one** disposition:

| disposition | means | required fields |
|---|---|---|
| `delivered` | the bytes reach a tree scaffolded through **any** provider | `channel`, `kind` (`package` \| `in-code`), `evidence` |
| `provider-scoped` | the bytes reach only trees created by a named provider | `provider`, `kind` (`template-payload`), `evidence`, and exactly one of `tracked-by` or `accepted` |
| `withheld` | authored here, deliberately delivered nowhere | `reason`, `evidence` |
| `gap` | no channel of any kind exists | `tracked-by` |

`provider-scoped` is the disposition this item exists because of, and it is why the vocabulary is not
simply "has a channel / has no channel". `fs-gg-feedback-report` **does** have a channel — the `fs-gg-ui`
template emits it correctly and unconditionally. What it does not have is *reach*. A two-valued
vocabulary would let that class be declared "delivered" and would hide exactly the defect the gate is
for, so a `provider-scoped` entry must say, in the file, either who owes making it universal
(`tracked-by`) or why provider-scoped reach is correct for that class (`accepted`).

### The five classes, measured

| owner | scope | rows | disposition | why |
|---|---|---|---|---|
| `.github` | driver | 5 | `delivered` | `FS.GG.Drivers` package; `stage-drivers.py:56` `DELIVERED_SCOPES = {"driver"}` |
| `.github` | operator | 7 | `withheld` | ADR-0057 operator skills run only in an operator checkout; every row's `materializes-when` is `false` |
| `fs-gg-sdd` | process | 16 | `delivered` | `SeededSkills.skillNames`, a hard-coded in-code list in the materializer |
| `fs-gg-game` | product | 11 | `delivered` | `FS.GG.Game.Skills` package; the `game-skills` contract in `registry/dependencies.yml` |
| `fs-gg-rendering` | product | 18 | `provider-scoped` + `tracked-by` | the `fs-gg-ui` template payload only — **this item's subject** |
| `fs-gg-templates` | product | 6 | `provider-scoped` + `accepted` | the `FS.GG.Workspace.Template` package's own template payload |

**The `fs-gg-templates` row corrected a working assumption, and the correction is worth recording.** A
git-tree listing of `FS-GG/FS.GG.Templates` shows `template/product-skills/<id>/SKILL.md` sitting at the
repository root, **outside** every `templates/<name>/` template root, and every template's
`.template.config/template.json` declares a single `{"source": "./", "target": "./"}` with
`postActions: null`. Read from the tree alone, that class looks like a fourth instance of ADR-0063's
class, and this item nearly filed it as one.

It is not. `FS.GG.Templates.csproj:124-140` **projects** those bodies at pack time into
`content/templates/fs-gg-fable-game/.agents/skills/<id>/` and
`content/templates/fs-gg-fable-bindings/.agents/skills/<id>/`, so the restored template root does carry
them even though the git tree does not — and the csproj carries its own gate (lines 148-156) reddening
when an authored body has no package item, because *"a skill authored under `template/product-skills/`
with no package item is packed into NO template and silently reaches no product."* That class is
`provider-scoped` with `accepted`: its rows' predicates are `template in [fable-game, fable-bindings]`,
so provider-scoped reach is what those rows *mean* — a `fable-bindings` skill on a rendering app tree
would be `[unexpected]`, not missing.

Two consequences, both deliberate. First, `evidence:` is a required field on every disposition
precisely because tree shape is not evidence of delivery; the entry must name the artefact that packs,
embeds, or withholds the bytes. Second, the arm does **not** try to verify a declared channel offline —
`SB-105` — because doing it from tree shape is what would have produced a false fourth instance here.

## User Stories

- US-001 (P1): As a maintainer adding or reviewing a `registry/skills.yml` row, I get a red gate when
  that row's class has no delivered byte channel, so I cannot ship a fourth supplied-from-nowhere skill
  without an explicit, recorded decision.
- US-002 (P1): As a reader asking "what carries these bytes to a scaffold?", I read one declaration that
  answers per class, instead of reconstructing it from three ADRs, four closed issues, and a consumer
  repository's embedded resources.
- US-003 (P2): As the reviewer of this item, I can see which acceptance criteria `.github` discharged
  itself, which are routed to filed receiver rows and why, and the disposition of every Rendering-owned
  product row rather than of the one whose absence happened to be detectable.

## Acceptance Scenarios

- AC-001 [US-002] [FR-001]: Given `registry/skills.delivery-channels.yml`, when a reader looks up any
  `(owner, scope)` class present in `registry/skills.yml`, then the file states exactly one of the four
  dispositions — `delivered`, `provider-scoped`, `withheld`, `gap` — with that disposition's required
  fields, and every disposition that leaves a class short of universal reach names either the issue that
  owes closing it or the reason the shortfall is correct.
- AC-002 [US-001] [FR-002]: Given a `registry/skills.yml` carrying a class with no entry in
  `registry/skills.delivery-channels.yml`, when `scripts/fsgg-skill-registry-check` runs, then it reports
  a `delivery-channel` finding naming that class and the rows in it, and exits non-zero.
- AC-003 [US-001] [FR-003]: Given an entry in `registry/skills.delivery-channels.yml` matching no row in
  `registry/skills.yml`, when the check runs, then it reports a `delivery-channel` finding naming that
  dead entry, so the declaration cannot rot into a restatement of a class that no longer exists.
- AC-004 [US-001] [FR-004]: Given an entry missing a field its disposition requires — a `gap` or an
  unaccepted `provider-scoped` entry with no `tracked-by`, a `tracked-by` that is not an
  `owner/repo#number` reference, a `withheld` entry with no `reason`, or a `delivered` entry with no
  `channel`/`evidence` — when the check runs, then it reports a `delivery-channel` finding, so no
  disposition can be asserted without the fact that makes it accountable.
- AC-005 [US-001] [FR-005]: Given no producer checkout and no network, when the arm runs, then it
  reaches a verdict from `registry/skills.yml` and `registry/skills.delivery-channels.yml` alone.
- AC-006 [US-001] [FR-006]: Given `tests/skill-registry/run.sh`, when the suite runs, then it exercises
  the arm's green case and each red case above, and the recorded gate-inversion evidence shows the exact
  mutation that removes the `fs-gg-rendering` / `product` entry and the red the suite then observed.
- AC-007 [US-003] [FR-007]: Given this package, when a reviewer asks which route was chosen and why,
  then `## Route decision` states it with the ADR-0063 / ADR-0058 / ADR-0062 argument and the
  `.github#1308` precedent.
- AC-008 [US-003] [FR-008]: Given this package, when a reviewer asks what happens to the other 17
  Rendering-owned product rows, then `## Disposition of the Rendering-owned product rows` answers for
  all 18 by name, with the measured predicate that justifies each disposition.
- AC-009 [US-003] [FR-009]: Given `.github#2545` acceptance criterion 2, when a reviewer asks where the
  byte channel is actually implemented, then this package names the filed receiver rows at the byte
  owner and the consumer, with the dedupe read that preceded filing.
- AC-010 [US-001] [FR-010]: Given a pull request that edits `registry/skills.delivery-channels.yml` and
  nothing else, when CI selects workflows, then `skill-registry-coherence.yml` runs, so the file cannot
  be edited without the gate that reads it.

## Functional Requirements

- FR-001: `registry/skills.delivery-channels.yml` declares, for each `(owner, scope)` class, exactly one of the dispositions `delivered`, `provider-scoped`, `withheld`, or `gap`, and any disposition short of universal reach carries either `tracked-by` (an `owner/repo#number` reference) or an `accepted` rationale. (Stories: US-002; Acceptance: AC-001)
- FR-002: The `delivery-channel` arm reports a finding for every `(owner, scope)` class present in `registry/skills.yml` with no entry in the declaration. (Stories: US-001; Acceptance: AC-002)
- FR-003: The arm reports a finding for every declaration entry matching no row in `registry/skills.yml`. (Stories: US-001; Acceptance: AC-003)
- FR-004: The arm reports a finding for an entry that is missing a field its disposition requires, carries a malformed `tracked-by`, declares no disposition, or declares one outside the vocabulary. (Stories: US-001; Acceptance: AC-004)
- FR-005: The arm runs offline from the two declaration files alone — no producer checkout, no network. (Stories: US-001; Acceptance: AC-005)
- FR-006: `tests/skill-registry/run.sh` exercises the arm's green case and each red case, and the package records the gate-inversion mutation and the observed red. (Stories: US-001; Acceptance: AC-006)
- FR-007: The route decision and its ADR-0063 / ADR-0058 / ADR-0062 consistency argument are recorded in this package. (Stories: US-003; Acceptance: AC-007)
- FR-008: The disposition of all 18 Rendering-owned product rows is recorded, each with the measured predicate justifying it. (Stories: US-003; Acceptance: AC-008)
- FR-009: The receiver rows required by the chosen route are filed at the byte owner and the consumer, deduped over REST, and their numbers recorded here. (Stories: US-003; Acceptance: AC-009)
- FR-010: `.github/workflows/skill-registry-coherence.yml` selects `registry/skills.delivery-channels.yml` on both `pull_request` and `push: main`. (Stories: US-001; Acceptance: AC-010)

## Disposition of the Rendering-owned product rows

`.github#2545` acceptance criterion 4 asks whether the other Rendering-owned product rows are in scope
of the same channel or deliberately profile-gated out. **They are all in scope of the same channel, and
none is deliberately excluded.** The measured facts:

`registry/skills.yml` carries **18** rows with `scope: product, owner: fs-gg-rendering` — not the 28 the
item body estimated. (The item body's "27 others" counted the Rendering and Game product rows together
before `.github#2547` added the six `fs-gg-templates` rows; the corrected count is 18 Rendering rows, so
**17** others.) All 18:

| id | `materializes-when` | in scope of the channel | detectable absence today |
|---|---|---|---|
| `fs-gg-feedback-report` | `always` | yes | **yes** |
| `fs-gg-scene` | `profile in [app, headless-scene, governed, sample-pack, game]` | yes | no |
| `fs-gg-testing` | `profile in [app, headless-scene, governed, sample-pack, game]` | yes | no |
| `fs-gg-project` | `profile in [app, headless-scene, governed, sample-pack, game]` | yes | no |
| `fs-gg-symbology` | `profile in [app, sample-pack, game]` | yes | no |
| `fs-gg-elmish` | `profile in [app, sample-pack, game]` | yes | no |
| `fs-gg-skiaviewer` | `profile in [app, sample-pack, game]` | yes | no |
| `fs-gg-layout` | `profile in [app, game]` | yes | no |
| `fs-gg-keyboard-input` | `profile in [app, game]` | yes | no |
| `fs-gg-styling` | `profile in [app, game]` | yes | no |
| `fs-gg-ui-widgets` | `profile in [app, game]` | yes | no |
| `fs-gg-game-shell` | `profile in [app, game]` | yes | no |
| `fs-gg-collision` | `profile in [game, sample-pack]` | yes | no |
| `fs-gg-grids` | `profile in [game, sample-pack]` | yes | no |
| `fs-gg-line-drawing` | `profile in [game, sample-pack]` | yes | no |
| `fs-gg-visibility` | `profile in [game, sample-pack]` | yes | no |
| `fs-gg-symbol-design` | `profile in [game, sample-pack]` | yes | no |
| `fs-gg-samples` | `profile == sample-pack` | yes | no |

**Why "in scope" and "detectable" differ, and why the difference is not a reason to narrow the channel.**
A tree scaffolded through a non-rendering provider carries no `profile` parameter at all;
`scripts/skill-union-assert.sh`'s `eval_clause` resolves an absent parameter to the empty string, so
every `profile`-gated predicate is false there and its absence is classified as a *justified* omission.
`always` is the only predicate that evaluates true, which is why exactly one row surfaced
(`work/2380-feedback-report-materialization/spec.md` F5).

That is a fact about **detectability, not about intent**. The moment a provider does supply
`profile=game` — which is the whole point of the `fable-game` family — every `game`-gated row above
evaluates true and has the same missing channel. Declaring 17 of them "deliberately profile-gated out"
would be inventing an intention no artefact records, and would leave the fabric one provider parameter
away from 17 simultaneous instances of the same defect. The channel therefore covers the class, not the
row; the gate is keyed on `(owner, scope)` for exactly this reason.

The latent hazard `#2380` F5 recorded — a negated predicate such as `lifecycle != spec-kit` fires on an
absent parameter — is noted and unchanged here. No current Rendering product row uses that form.

## Filed receiver rows

`.github#2545` acceptance criterion 2 asks that the chosen channel be implemented. `.github` owns
neither the bytes nor the materializer, so — exactly as `.github#1308` routed the game class to
FS.GG.Game#449 — the implementation is filed at the two repositories that do own it. Each was deduped
over REST against the **cause** before filing; all three are on the Coordination board at `Backlog`.

| Row | Owns | Blocked by |
|---|---|---|
| [FS-GG/FS.GG.Rendering#1240](https://github.com/FS-GG/FS.GG.Rendering/issues/1240) | publishing `FS.GG.Rendering.Skills` — the byte source, and the root of the chain | — |
| [FS-GG/FS.GG.SDD#864](https://github.com/FS-GG/FS.GG.SDD/issues/864) | the fourth enrollment channel in the scaffold materializer, mirroring `GameSkills.fs` | FS.GG.Rendering#1240 |
| [.github#2639](https://github.com/FS-GG/.github/issues/2639) | the `rendering-skills` contract row, and the `provider-scoped`→`delivered` flip in `registry/skills.delivery-channels.yml` | FS.GG.Rendering#1240 |

Dedupe evidence: `gh api search/issues` over `repo:FS-GG/FS.GG.Rendering` for `ADR-0063`,
`skills package`, `materializer`, `Rendering.Skills`, `product-skills`, `feedback-report`,
`delivery channel` — the ADR-0063 hits (`#965`, `#970`) retire the *frozen game* copies and are closed;
that repository had exactly one open issue (`#14`, a Renovate dashboard). `repo:FS-GG/FS.GG.SDD` for
`materializer`, `GameSkills`, `skill channel`, `feedback-report` — two open issues (`#839`, `#16`),
neither carrying this cause. `repo:FS-GG/.github` for `rendering-skills`, `delivery-channels`,
`owner-sourced`, `game-skills` — `#1299`/`#1300`/`#1308` are the closed driver and game instances.

`Blocked by` is written on the **Projects v2 field**, not as a body line: a `Blocked by:` line in an
issue body is inert, because nothing that clears a blocker reads the body
(`src/FS.GG.Coord.Core/Protocol.fs:680`, the `.github#1933` lesson). The `## Blocked by` headings in
those bodies are prose for a reader; the edges above are the field.

## Ambiguities

- AMB-001: **Which route.** Decided: Route B (`## Route decision`). This is the ambiguity
  `work/2380-feedback-report-materialization/clarifications.md` recorded as AMB-002 and deferred to this
  item. Resolved, not carried.
- AMB-002: **Where the class declaration lives** — a new top-level key in `registry/skills.yml`, or a
  separate file. Decided: a separate file, `registry/skills.delivery-channels.yml`. `registry/skills.yml`
  is the surface of the `registry-schema` contract (owner `sdd`, consumers `[github]`) and is read by
  FS.GG.SDD's typed `Fsgg.Registry` validator; adding an unknown top-level key there is a cross-repo
  schema change this item has no mandate to make, and `skill-registry-autofix.yml` rewrites that file
  unattended. A separate `.github`-owned file has neither hazard.
- AMB-003: **Whether to amend ADR-0063.** Decided: no. ADR-0063 states *"This ADR builds nothing"* and
  explicitly delegates transport per class to coordination rows (*"Transport decision — `.github#1300`
  is canonical"*). This item picks a transport for a third class under that existing delegation and adds
  a mechanical check; it changes no decision the ADR records. Recorded here rather than left silent, so
  a reviewer who disagrees can say so against a stated position.

## Public Or Tool-Facing Impact

- Adds one authored `.github`-owned declaration file, `registry/skills.delivery-channels.yml`.
- Adds one arm to `scripts/fsgg-skill-registry-check`, which is run by
  `.github/workflows/skill-registry-coherence.yml` on pull requests, `push: main`, and a nightly
  schedule. The arm is offline and adds no network or token requirement.
- Changes no published package, no registry row, and no cross-repo contract surface. The
  `registry/dependencies.yml` `rendering-skills` contract row that Route B eventually owes is
  deliberately **not** added here: `check-feed-coherence.py` asserts a contract's `package-version` is
  the newest version live on the feed, unconditionally, so declaring the contract before
  `FS.GG.Rendering.Skills` exists would red that gate. It is carried by the filed receiver row.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2545-rendering-owned-product-skill-channel`.
