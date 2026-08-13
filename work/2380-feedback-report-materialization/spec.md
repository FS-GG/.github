---
schemaVersion: 1
workId: 2380-feedback-report-materialization
title: Feedback Report Materialization
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Feedback Report Materialization Specification

Prose status: specified

## User Value

A worker or maintainer who finds `fs-gg-feedback-report` missing from a product tree can read one
established cause instead of re-deriving it, and can tell whether that tree is defective or simply
outside the skill's only delivery channel.

`.github#2366` measured the cost of not having this: eight of ten worker cycles rediscovering the same
friction. `.github#2380` recorded a fresh recurrence on 2026-08-13 — `EHotwagner/S.I.R.#193` could not
run `validate-feedback-state.py`'s required `feedback-tool.fsx` because the whole
`.agents/skills/fs-gg-feedback-report` directory is absent. Each rediscovery re-opens the same two
candidate mechanisms `#2380` names, and neither is correct, so each rediscovery also re-derives a wrong
model of the fabric.

The value delivered here is a *settled* cause with its measurements attached, and rows filed where the
mechanism actually lives, so the next worker reads instead of re-deriving.

## Scope

- SB-001: Establish, by measuring producers rather than inferring from the consumer, why
  `fs-gg-feedback-report` is absent from `EHotwagner/S.I.R.` — `.github#2380`'s acceptance criterion 1
  and, in the item's own words, the deliverable ("Root cause: Not established").
- SB-002: Adjudicate each of the two candidate mechanisms `#2380` names, as confirmed or refuted, each
  with the specific measurement that settles it.
- SB-003: Record the executed truth values of `materializes-when` predicates against the scaffold
  parameter set `S.I.R.`'s own `scaffold-provenance.json` actually carries, produced by running this
  repository's reference evaluator (`scripts/skill-union-assert.sh --eval-when`), never by reading it.
- SB-004: Identify the sole delivery channel for `fs-gg-feedback-report` and demonstrate, from
  `S.I.R.`'s own provenance attribution, that no channel present in its scaffold chain carries it.
- SB-005: State whether a second product tree must be measured to separate the candidates, and if not,
  why the producer-side evidence already separates them without one.
- SB-006: File every defect established outside this item's declared `Paths:` as its own row at its
  root cause, deduped over REST against that cause, and record the resulting issue numbers here.
- SB-007: Route `.github#2380` acceptance criterion 3 — the `EHotwagner/S.I.R.` remediation decision —
  as an explicit decision carrying a recommendation and its evidence.

## Non-Goals

- SB-101: Does NOT change any scaffold materializer, `dotnet new` template, producer manifest, or
  provider declaration. `.github` does not own them; `.github#2366` SB-005 and this item's own
  delivery-route re-affirmation both name remediation as staged consumer-side work.
- SB-102: Does NOT touch `EHotwagner/S.I.R.`. That tree is user-owned and not org-administered; whether
  it is remediated is a decision this package routes, not one it executes (`#2366` SB-006).
- SB-103: Does NOT edit `registry/skills.yml`, `scripts/skill-union-assert.sh`, or
  `registry/repos.yml`. None is in this item's declared `Paths:`; established defects in them are
  filed, not fixed here.
- SB-104: Does NOT re-open, re-litigate, or amend ADR-0063. This work finds a third instance of the
  class that ADR already decided; it does not propose a different decision.
- SB-105: Does not implement Governance policy enforcement.

## User Stories

- US-001 (P1): As a worker who finds `fs-gg-feedback-report` absent from a product tree, I can read one established cause with its measurements, instead of re-deriving it from the two candidates the item left open.
- US-002 (P1): As a maintainer deciding where to fix this, I can see which repository owns the mechanism and which already-tracked decision governs it, so I file against the cause rather than the surface.
- US-003 (P2): As the human owning the `EHotwagner/S.I.R.` posture, I am handed an explicit decision with a recommendation and its evidence, rather than silence.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given the record, when a reader looks for the cause of the absence, then exactly one root cause is stated and each load-bearing claim cites the artifact and the command that established it.
- AC-002 [US-001] [FR-002]: Given `#2380`'s two candidate mechanisms, when a reader consults the record, then each is marked confirmed or refuted and carries the measurement that settles it.
- AC-003 [US-001] [FR-003]: Given `S.I.R.`'s real recorded parameter set, when the record reports how `always` and a profile-gated predicate evaluate, then both values were produced by executing `scripts/skill-union-assert.sh --eval-when` and the invocation is reproduced.
- AC-004 [US-002] [FR-004]: Given the record, when a reader asks which channel should have delivered the skill, then the sole channel is named and `S.I.R.`'s own provenance attribution is shown to contain no channel carrying it.
- AC-005 [US-001] [FR-005]: Given the question of whether a second tree is required, when a reader consults the record, then it answers explicitly and justifies the answer from producer-side evidence.
- AC-006 [US-002] [FR-006]: Given a defect established outside the declared `Paths:`, when the record describes it, then it carries a filed issue number and the dedupe read that preceded filing.
- AC-007 [US-003] [FR-007]: Given acceptance criterion 3, when the record addresses `S.I.R.`'s posture, then a recommendation with evidence is routed as an explicit decision and is not silently resolved here.

## Functional Requirements

- FR-001: The record states exactly one established root cause and cites, per load-bearing claim, the artifact and command establishing it. (Stories: US-001; Acceptance: AC-001)
- FR-002: The record adjudicates both candidate mechanisms from `.github#2380` as confirmed or refuted, each with its settling measurement. (Stories: US-001; Acceptance: AC-002)
- FR-003: The record reports executed `--eval-when` truth values for `always` and for a representative profile-gated predicate against the real parameter set. (Stories: US-001; Acceptance: AC-003)
- FR-004: The record names the sole delivery channel and demonstrates its absence from `S.I.R.`'s provenance attribution. (Stories: US-002; Acceptance: AC-004)
- FR-005: The record answers whether a second product tree is required to separate the candidates, with justification. (Stories: US-001; Acceptance: AC-005)
- FR-006: Every defect established outside the declared `Paths:` carries a filed issue number and its dedupe evidence. (Stories: US-002; Acceptance: AC-006)
- FR-007: The `EHotwagner/S.I.R.` remediation decision is routed explicitly with a recommendation and evidence. (Stories: US-003; Acceptance: AC-007)

## Findings — the established cause

This section is the substance of the deliverable. Every claim carries its verification.

### F1 — `fs-gg-feedback-report` has exactly one materializing channel, and it is correct

`registry/skills.yml:216` declares the row `owner: fs-gg-rendering`, `source:
FS.GG.Rendering/template/feedback-report/skill/SKILL.md`, `materializes-when: "always"`. The producer
is **FS.GG.Rendering**, not `FS.GG.Templates` or `FS.GG.SDD` — the two repositories `#2380`'s candidate
list names.

FS.GG.Rendering's `fs-gg-ui` template emits it **unconditionally and correctly**. Its
`.template.config/template.json` carries a `sources` entry with `source:
"template/feedback-report/skill/"`, `target: ".agents/skills/fs-gg-feedback-report/"`, and **no
`condition` key at all**. Its own comment records that this is deliberate and already once repaired:

> "Issue #434: … carries NO condition at all — it materializes on EVERY generated workspace (manifest
> materializes-when: always). … It was gated on `feedback` until #434 — which, since `feedback`
> defaulted to false, shipped it to nobody."

So the historically plausible mechanism — a `feedback`-parameter gate defaulting false — is real, is the
*former* cause, and was fixed in Rendering before this item was filed.

*Verification:* `registry/skills.yml:216`;
`gh api repos/FS-GG/FS.GG.Rendering/contents/.template.config/template.json?ref=main`, base64-decoded
to the repository's own bytes → the `sources` entry at lines **500–507** (`source` 501, `target` 502,
no `condition` key in the object) and the quoted comment at line **506**.

### F2 — `EHotwagner/S.I.R.` was not created by that template

`S.I.R.`'s `.fsgg/scaffold-provenance.json` records `templateRef:
"FS.GG.Workspace.Template::0.8.0#fs-gg-fable-game"`, `providerName: "fable-game"`,
`providerContractVersion: "1.1.0"`, `generator: FS.GG.SDD.Artifacts 1.0.0`, `outcome:
"consumerMigration"`.

The `fs-gg-fable-game` template is not a skill-materializing template. Its
`.template.config/template.json` (identity `FS.GG.Workspace.Template.FableGame`) has a **single**
`sources` entry `{"source": "./", "target": "./"}` carrying only an `exclude` list, **`postActions:
null`**, and `symbols` that are naming-only — `productName`, `productNameTrimmed`, `effectiveName`,
`effectiveNameLower`, `rootNamespace`, `rootNamespaceTrimmed`, `effectiveIdentifier`. It declares **no
`profile`, `lifecycle`, `feedback`, or `designSystem` parameter**, which are the four the registry says
its predicates are evaluated against (`registry/skills.yml:96`).

No template in `FS.GG.Templates` carries a skill root at all: a recursive tree listing yields zero
paths matching `^templates/.*(\.agents|\.claude|skill)`.

*Verification:* `gh api repos/EHotwagner/S.I.R./contents/.fsgg/scaffold-provenance.json`;
`gh api repos/FS-GG/FS.GG.Templates/contents/templates/fs-gg-fable-game/.template.config/template.json?ref=main`
parsed for `identity`/`sources`/`postActions`/`symbols`;
`gh api repos/FS-GG/FS.GG.Templates/git/trees/main?recursive=1` (369 paths, `truncated: false`) filtered
as above → no matches.

### F3 — the three channels that *did* write `S.I.R.` carry no Rendering-owned product skill

`S.I.R.`'s provenance attributes every path it received:

| Field | Count | What it carries |
|---|---|---|
| `producedPaths` | 15 | product source only (`src/…`, `tests/…`, `build.sh`) — no skills |
| `driverPaths` | 42 | `.github` drivers: `work-board`, `work-roadmap`, `padd-item`, `work-board-best`, `work-board-normal`, into both `.agents` and `.claude` |
| `gameSkillPaths` | 2 | exactly one product skill — `fs-gg-game-fable` |
| `mirroredPaths` | 1 | the same `fs-gg-game-fable` |
| `sddOwnedPaths` | 2 | `work/…`, `readiness/…` |

`FS.GG.SDD` supplies exactly three enrollment channels —
`src/FS.GG.SDD.Commands/CommandWorkflow/SeededSkills.fs`, `DriverSkills.fs`, and `GameSkills.fs`.
`SeededSkills.skillNames` is a **hard-coded in-code list of the sixteen `fs-gg-sdd-*` process skills**;
its own comment calls it "the single in-code source of the set". `DriverSkills` and `GameSkills` read
**embedded assembly resources** (`Driver.manifest` / `Driver.skill/`, `GameSkill.manifest` /
`GameSkill.skill/`). None reads `registry/skills.yml`, and none has a Rendering-product channel.

`fs-gg-feedback-report` appears **nowhere in FS.GG.SDD's entire tree** — zero matches for `feedback`
across 2558 paths.

*Verification:* the provenance fields above;
`gh api repos/FS-GG/FS.GG.SDD/contents/src/FS.GG.SDD.Commands/CommandWorkflow/SeededSkills.fs?ref=main`
(lines 27–43 are the literal list); same for `GameSkills.fs` (`manifestResourceName =
"GameSkill.manifest"`, line 30) and `DriverSkills.fs` (`manifestResourceName = "Driver.manifest"`,
line 25); `gh api repos/FS-GG/FS.GG.SDD/git/trees/main?recursive=1` → 2558 paths, `grep -i feedback`
→ no matches.

### F4 — emission is not predicate-driven, and the decisive proof is a skill that *was* delivered

`fs-gg-game-fable` was materialized into `S.I.R.` (sha256 `443a82d2…`, matching
`registry/skills.yml:207`). Its registry predicate is `profile in [game, sample-pack]`. Executed
against `S.I.R.`'s real parameter set, that predicate is **false**.

A skill whose predicate evaluates false was delivered; a skill whose predicate evaluates true was not.
Emission therefore does not consult `materializes-when` at all — in *either* direction. The registry
predicate is **descriptive, not causative**: it is read only by the consumer-side audit gate
(`scripts/skill-union-assert.sh` check 4), never by any producer.

### F5 — why `fs-gg-feedback-report` is the only row that surfaces

`scripts/skill-union-assert.sh`'s `eval_clause` resolves a parameter as `"${PARAM[$key]-}"`, so a
parameter the scaffold does not carry is the **empty string**. Measured against a `fable-game`-shaped
parameter set:

| Predicate | Result |
|---|---|
| `always` | **true** |
| `profile in [app, headless-scene, governed, sample-pack, game]` | false |
| `profile in [game, sample-pack]` | false |
| `profile == sample-pack` | false |
| `template in [fable-game, fable-bindings]` | false |
| `lifecycle != spec-kit` | **true** |

Under check 4's classes, `declared ∧ condition TRUE ∧ absent-everywhere` is `[missing]` (FAIL), while
`declared ∧ condition FALSE ∧ absent` is a *justified* omission. `fs-gg-feedback-report` is the only
**product** row in the catalog whose predicate is `always`. Every other product row is `profile`-gated
and therefore silently justified on a tree with no `profile` parameter. That is precisely why one and
only one skill surfaces — the absence is not special, its *detectability* is.

The `lifecycle != spec-kit` row is recorded because it shows the same empty-string resolution makes a
negated predicate fire on an absent parameter. No current product row uses that form; it is a latent
hazard, noted, not claimed as active.

*Verification:* `scripts/skill-union-assert.sh:314–339` (`eval_clause`; the parameter lookup
`"${PARAM[$key]-}"` is at 326, 332 and 336) and `:719–720` (the `[missing]` class); each row above
produced by `scripts/skill-union-assert.sh --eval-when '<predicate>' --params <provenance>`, exit 0.

### F6 — the established root cause

> `fs-gg-feedback-report` was never *skipped* by a materializer that considered it. **No materializer
> in `S.I.R.`'s scaffold chain was ever responsible for it.**
>
> The skill's only delivery channel is FS.GG.Rendering's `fs-gg-ui` `dotnet new` template. A product
> scaffolded through a **non-rendering provider** (`fable-game`) never restores that template, so the
> row has no channel to that tree. `materializes-when: always` records an org-wide intent that only
> one template family implements, for trees only that family creates.

This is a **third instance of the class ADR-0063 already named** — *"declared ∧ gated-in ∧
supplied-from-nowhere … the `owner`/`source` fields name where the bytes are, and nothing reads them
for delivery."* The first two were `fs-gg-playtest` (`.github#1299`) and the `workRoadmap` driver
(`.github#1300`).

Both of those are **closed**, and their fix demonstrably works: `S.I.R.`, scaffolded 2026-08-10 —
after both closed on 2026-07-21 — received 42 `driverPaths` (the `#1300` class) and a
`gameSkillPaths` delivery (the `#1299` class). The ADR-0063 fabric was built and enrolls skills **by
class**: `scope: driver` and game-owned product rows each got an embedded channel.

`fs-gg-feedback-report` belongs to neither enrolled class. It is `scope: product, owner:
fs-gg-rendering` — the class the fabric still assumes is supplied by a restored *rendering* provider
template. Under a non-rendering provider that assumption is vacuous. Its `source:` path compounds this:
it is `template/feedback-report/skill/`, **outside** `template/product-skills/`, the directory the
frozen provider cut copies — an anomaly `registry/skills.yml`'s own header flagged at `.github#298`
("supplied from a `template/<feature>/skill/` path rather than the usual
`template/product-skills/<id>/`") without ever tracing its consequence.

So the residual gap is not a new decision. It is **ADR-0063's decision, not yet extended to the
Rendering-owned product class**.

### F7 — adjudication of `#2380`'s two candidates

**Candidate 1** — *the `fs-gg-fable-game` template identity may not emit `fs-gg-feedback-report` into
product skill roots for that profile.* **REFUTED as stated.** It is not profile-specific and not
specific to this skill: that template has **no `profile` parameter at all** and emits **no skills of
any kind** (F2). There is no profile axis on which it could discriminate.

**Candidate 2** — *the producer-side skill mirror may not read or honour `registry/skills.yml`'s
`materializes-when` at all for this skill, independent of profile.* **REFUTED as stated, and the true
statement is broader.** The mirror that ran (`SeededSkills`) is a hard-coded sixteen-name process list
that never had this skill in view (F3); and the producer that *does* own the skill, Rendering's
template, honours `always` correctly (F1). What is true is stronger and not skill-specific: **no
producer anywhere in the org reads `materializes-when`** — proven not by absence but by
`fs-gg-game-fable`, delivered while its predicate evaluates false (F4).

### F8 — is a second product tree required?

**No.** `#2380` proposed a second tree because it framed the question as "is this profile-specific,
skill-specific, or broader?" — a question about the *distribution* of a symptom. Measuring the
producers answered the *mechanism* directly, which subsumes it: a channel that does not exist for a
provider family cannot deliver to any tree in that family, for any profile or skill.

A second tree would add confirming instances of a cause already established, at the cost of
measuring another user-owned repository. It would become necessary only if the claim in F2 were
contested — that `fs-gg-fable-game` emits no skills — and that claim is settled from the template
definition itself, not from any tree.

What a second tree *would* usefully establish is **blast radius**, which this record deliberately does
not claim: the number of affected trees is unknown here, and `registry/repos.yml` currently records
`sir` as `role: non-participant, receives: []`.

## Filed rows

Every row below was deduped over REST against its **cause** before filing, and each is on the
Coordination board at `Backlog`. None is fixed in this package.

| Row | Cause | Discharges |
|---|---|---|
| `.github#2545` | No delivered channel for Rendering-owned `scope: product` rows, so a non-rendering-provider scaffold receives none of them — the F6 root cause, a third instance of ADR-0063's class | `#2380` AC2 |
| `.github#2546` | D1 below — `--params` cannot parse the provenance shape actually emitted | new |
| `.github#2547` | D2 below — `registry/skills.yml` omits `FS.GG.Templates` entirely | new |
| `.github#2548` | The `EHotwagner/S.I.R.` remediation decision, plus the `registry/repos.yml` `sir` link | `#2380` AC3 + AC4 |

`#2545` deliberately does **not** pick between the two fix routes it names; ADR-0063 already decided
the principle, and choosing the transport for this class is that row's first acceptance criterion.
`#2548` is a `Class: decision` row because `EHotwagner/S.I.R.` is user-owned and not org-administered,
so no worker can settle it.

`#2380`'s AC4 could not be discharged inside this item: `registry/repos.yml` is not in its declared
`Paths:`, and the `sir` row's correct content depends on `#2548`'s outcome. It is carried by `#2548`
acceptance criteria 3 and 4 rather than left silent.

## Defects established outside this item's declared `Paths:`

Recorded here; filed per SB-006 as `.github#2546` and `.github#2547`. Neither is fixed in this package.

- D1 (`.github#2546`): `scripts/skill-union-assert.sh --params` **cannot run against a real
  workspace-template tree.**
  `load_params` requires `.effectiveParameters | type == "object"`, but the provenance
  `FS.GG.SDD.Artifacts` emits is an **array of `{key,value}` objects**. Measured against `S.I.R.`'s
  real file: exit **2**, `::error::skill-union-assert: params has no .effectiveParameters object`.
  ADR-0017's condition-aware check — the one arm designed to report exactly this `[missing]` — has
  therefore never been executable against a product of this provider family.
- D2 (`.github#2547`): `registry/skills.yml` is **incomplete with respect to `FS.GG.Templates`.** That repository ships
  a producer manifest (`template/skill-manifest/skill-manifest.json`) declaring six `product` skills
  (`fable-bindings`, `fable-interop`, `fable-project`, `fable-remoting`, `fable-signalr`,
  `fable-testing`) whose predicates use a parameter **`template`** that `registry/skills.yml:96` does
  not declare. None of the six appears in the catalog, and `FS.GG.Templates` appears nowhere in it as
  an owner. The `declared-completeness` arm that caught `fs-gg-feedback-report` at `.github#298` does
  not reach this producer.

## Ambiguities

Three were carried and are recorded in `clarifications.md`: whether a second product tree must be
measured (AMB-001, decided no), which fix route to take (AMB-002, deferred to `.github#2545`), and
whether to widen to reach `registry/repos.yml` (AMB-003, decided no). None remains blocking.

One fact is deliberately **not** claimed: the number of product trees affected. One tree was measured;
this record establishes a mechanism, not a population.

## Public Or Tool-Facing Impact

- This specification is an SDD lifecycle artifact and command-report contract input.
- It changes no tool, contract, schema, or published surface. The package is a record plus filed rows.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2380-feedback-report-materialization`.
