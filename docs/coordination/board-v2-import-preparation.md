# Coordination V2 bounded import and pilot

This document records the historical import/v1 pilot and bounded qualification windows below.
Their selected counts, field snapshots and “current” references belong to those original windows;
they are not the final programme-v4 enrollment or a fresh native observation. The current programme
contract uses the same protected manifest path for a closed two-field public commitment, with the
complete enrollment retained in the private binding. See the [owning cutover plan](2026-09-29-coordination-v2-board-design.md)
and [maintained caller contract](../../tests/coord-board-v2-cli-join/INSPECT.md).
`tools/board-v2-import.schema.json` continues to describe historical import/v1 only. The maintained
constructor validates current public commitment shape separately; neither public shape validation
nor offline pair preparation authenticates private enrollment or authorizes native changes.

**COORD-BOARD-V2-01.2 passed on 2026-10-02.** Root created and read back
[Coordination V2, Project 3](https://github.com/orgs/FS-GG/projects/3), established the four planning
fields and four filtered views, and applied the three approved existing native issues. Independent
readback verified each issue identity, one membership and all four field values. An explicit repeat
apply emitted **zero mutation intents**. The [canonical plan](2026-09-29-coordination-v2-board-design.md)
retains .2 acceptance; the independently qualified root-local .3 route is recorded below. Broader
broader carryover and consumer adoption remain open in .4; .5 publication is complete and .6 product adoption remains separate.

The original pilot manifest at protected `351e5db40d4d72abd6e87882614725305eb2a419` recorded
**eleven candidates, three approved pilot items, two omissions and six adjudication gaps**. The historical four-target successor appended .github#3009: **twelve candidates and four
selected targets**. The [current manifest](board-v2-import-manifest.json) now selects three active
targets and retains closed .github#3009 as omitted delivery history. The independently qualified bounded .4 operation is recorded below; broader
carryover remains separate. Original three
issue objects, historical operation/provenance and receipts remain unchanged. `preparationState=adjudication-required` remains accurate
for the unselected inventory. No issue was created, closed, reopened or converted from a PR. Legacy
Project 1 and its separately scoped archive operation were preserved.

## Selected administration exception

The user explicitly authorized target creation, schema establishment, cohort selection, apply and
readback. Root selected a bounded native one-shot administration exception for these effects. For this pilot, that selection
supersedes the earlier preparation's sole shared-transport route. It does not authorize or qualify the
.3 automatic writer, whose production fence remains **Unavailable**.

Root ran a private F# script using `HttpClient` and `System.Text.Json`, with the existing organization
Projects credential supplied in memory/environment. The script loaded the protected-source pure F#
constructor before apply. It did not use the internal transport port or synthesize a grant. No native
issue lifecycle/dependency, legacy-project, source-ref, permission or settlement writes were requested.
A linear manual operator needs no durable automatic retry service or new lifecycle model.

Each invocation retained private, numbered intents, responses, complete snapshots and receipts. Token
values, raw private paths and full account data are excluded from this source report. Complete reads
bound membership to 50 entries/page and ten pages, native dependencies to 50/item, and the pilot to
five items. Each stage allowed one writer, a 300-second deadline, 25-second HTTP timeout, 180 calls
and zero automatic read or mutation retries. Unknown or conflicting evidence stopped the stage.

## Exact target and schema

The target is organization `FS-GG` / `O_kgDOEYAWYw`, Project 3 `Coordination V2` /
`PVT_kwDOEYAWY84Bldpa`. Actual complete owner/title/number/node-ID readback supersedes the earlier
inaccessible-node observation; no title fallback or Project 1 substitution occurred. Legacy Project 1
remains `PVT_kwDOEYAWY84Bb08W`. The manifest contains the actual field and option IDs.

| Field | Type and values | Owner after initial seed |
|---|---|---|
| Status | single select: Backlog, Ready, In progress, Blocked, Done | Scheduling owner |
| Roadmap | text: owning outcome/acceptance link | Owning plan owner |
| Track | single select: Active delivery, Follow-up | Scheduling owner |
| Observation | single select: Verified, Stale, Unknown | Restricted projection |

The four canonical table views were independently read back with exact filters: Active delivery
(`track:"Active delivery" -status:Done`), Blocked (`status:Blocked`), Backlog (`status:Backlog`) and
Follow-up (`track:"Follow-up"`). The default View 1 was retained. Actual API introspection confirmed
`visibleFieldIds` in view configuration and `filter` on the top-level view update input; filters were
written through that supported route. Native Title/Repository/Assignees remain issue-derived.

## Approved cohort and bounded adjudication

| Existing issue | Representative remaining outcome | Current owner and next action |
|---|---|---|
| [SDD#928](https://github.com/FS-GG/FS.GG.SDD/issues/928) | Source contract and provider-authoring SDK/harness | SDD provider-contract owner: finish accepted architecture records, implement and publish the ecosystem-neutral descriptor contract and SDK. |
| [Templates#441](https://github.com/FS-GG/FS.GG.Templates/issues/441) | Provider package publication | Templates provider/package owner: after compatible SDD/Governance contracts publish, qualify and publish TypeScript, JavaScript, Rust and Go provider packs and skills. |
| [.github#3010](https://github.com/FS-GG/.github/issues/3010) | Public installed catalog adoption | Scaffolder/registry owner: after exact catalog publication, replace the fixed wizard list with descriptor discovery and qualify all four installed providers. |

All three are **Blocked / Active delivery / Verified**, with Roadmap pointing to
[the owning polyglot outcome](https://github.com/FS-GG/.github/issues/3008). Verified means the selected
observations are current; it does not mean delivery is complete. Fresh REST 200 reads confirmed open
native issue state, unchanged node IDs and update revisions. Complete native `blocked_by` reads were
empty for each issue. Owning-plan prerequisites remain recorded separately and are not fabricated as
native dependency edges. Status remains Blocked because accepted source/publication/adoption work
is unfinished, including prerequisite plan work.

The manifest retains historical candidates: `.github#2954` is omitted as superseded and `.github#2963`
as delivered at the selected V2 profile. `.github#2994` is a non-schedulable epic; `.github#2995–2997`,
`Coordination#24` and `Templates#438` retain current-scope/acceptance adjudication gaps. Templates#438's
old Game 0.13→0.14 source request is superseded by the observed Game 0.16.0 pin; historical publication
acceptance was not requalified. No remaining outcome is inferred solely from open native state.

Other outcome slots remain outside the cohort. In particular, protected revision `3aa00f2` records
shared WASM 0.1.1 publication and installed acceptance; it supplies no new native issue mapping for
this pilot. SC2, BAR, LEARN and installed-host/W6/player-study slots retain their owning work and
unresolved evidence. Routine source work can continue without board membership.

## Actual apply, delayed reads and retained extra membership

Exactly three add requests were issued for the three selected native node IDs. Two immediate complete
membership snapshots lagged acknowledged adds, so their stages stopped before further seeding. Fresh
independent reads later confirmed the expected memberships; explicit continuation seeded absent values
and preserved existing values. Neither delay caused a blind add retry. Final readback verified all
three memberships and Status, Roadmap, Track and Observation. The explicit repeat retained all twelve
existing planning values and emitted zero mutation intents.

Complete project readback contains **four memberships**. The extra
[SDD#935](https://github.com/FS-GG/FS.GG.SDD/issues/935), node `I_kwDOS_PboM8AAAABOda86Q`, is a native
child of SDD#928 and appeared without an operator add request. It was retained unapproved and without
planning seeds. Native-child project propagation is a plausible inference; these observations do not
prove the cause or actor. It is not a fourth pilot item or broader-import approval. The automatic
writer's population policy must treat this distinction explicitly during .3 qualification.

The final independent readback with the corrected official locked constructor has receipt SHA-256
`0bb6e7131ecaa4d854c873a6ba60762e76936b620a52b49b49b209dfe050d2ad`. The earlier effects readback remains retained as
`a6eea3a4b0f84e3cb87506eccaa35449a197d37d3664edba5bbba020ca21d3af`.
The repeat's complete membership SHA-256 is
`d7ee7927db68ef21002b18f4ffde07dca4826dd1ad5452b0ff0e532b20c0c475`.
The manifest retains exact issue/item IDs, field/option/view IDs and receipt hashes. Private originals
remain retained by root; there is no public CI run for this manual native operation.

## Constructor and operator provenance

The [protected pure constructor](https://github.com/FS-GG/.github/blob/47d0a38ca1f3b390bb20b88f04c8ba019d989c15/tools/BoardV2Import/Program.fs)
validates the bound manifest and emits fixed descriptive stages; it has no transport or writer.
`pilotPlan` copies the selected complete issue objects, including their flat planning seed keys.
Source program SHA-256 is `c0497c92485c4f9d6e03d8c8220b3c4203929aa7c8832241e785b372d36404ae`.
The private operator is a separate artifact and is not described as an installed protected assembly.

Initial constructor build failed `NU1403`. A separate unlocked restore produced the historical local
constructor `197dc5d65875bdd5321ec8471e22d577156a4d89a8819ccd77a94244fca85aee`, used with operator
`1de4ad11ea189d27bef554ac45575f27cca05d41c942a0942a56080c8fffbd48` for the actual effects.
The protected tracked lock stayed unchanged, but the separate fresh lock used SDK/cache metadata
`IGqjL9U8pQl7CaCpFP3f5slZMduE8c7ZBx7HycAQ9dc1tawyniNyw7Fh6L2cShIoQDcaDL+S8/kokhWs8WwnBQ==`.
Its compiled FSharp.Core DLL hash `9abd1f889fc8e2d2a74d6ac83a31e6c3012c075a17946a7d687c0cbe33ed15be`
differed from official NuGet bytes. These historical artifacts and receipts are preserved; they do
not establish official-package or installed production-artifact qualification.

Root selected correction of that dependency closure. A fresh private package cache, official-only
NuGet source mapping and disabled implicit SDK library-packs source restored FSharp.Core **10.1.401**
in locked mode against the unchanged protected content hash
`/lvOK51KD8IangBYkfDVna3/HrE/r4VUnX3dI01KDspctoYXmind3POeoYkqWKeXHwiKlf6Hc9TxwSnoSCWcjw==`.
The isolated official package's DLL SHA-256 is
`39b0b7f06c11bedd93f94b6f101fffd645f1c4437a5a6d3ee79259f855aeb11a`.
The same protected source then built constructor
`dac15107412e92e9238f232008137244019957b7b57ab164bcd58b647a5227d3`; only the private operator's
constructor path/hash selection changed, yielding operator
`81c192f5e12d22783e4e85c345a2578f361e390a895740239d82d286cd66f474`.
SDK 10.0.401 targets net10.0; the observed FSI host runtime is 10.0.12. FSI uses its own SDK FSharp.Core
hash `2232698f623c28b8bcca32fa3cc8943cdd8529b20b487b5938106aae1c2ab256`; the compiled constructor
package closure and the FSI host are recorded separately. No shared cache was altered.

The corrected constructor passed a fresh dry-run and independent native readback against the actual
three-item pilot; both invocations emitted zero mutation intents. The final bound-manifest SHA-256 is
`28563970466d2543d88b3ec670d08ef02ee4bf83fe1dd991d9c77100ed6daf94`. This requalification did not replay apply or erase historical
artifact provenance. Official locked construction and manual pilot readback are verified; installed
production writer qualification remains .3.

## Historical .2 next frontier — superseded by the .3 qualification below

## Consumer inventory and ownership

| Consumer | Current binding and disposition |
|---|---|
| `Board.fs/.fsi` | `exact-project1` stays pinned to legacy organization/project identity. Preserve legacy behavior. |
| `V2Projection.fs/.fsi` | Protected source from #3987 provides restricted exact binding and Observation-only behavior; .3 owns immutable item-target verification and actual qualification. Source fixtures are not live activation evidence. |
| `FS.GG.Coord.Cli/Client.fs` and legacy lifecycle adapters | Retain their legacy schema/lifecycle meanings; no redirection to the planning schema. Root owns shared CLI registration. |
| `Cache.fs/.fsi` | Existing owner/title board-map keys are legacy hints. V2 requires independent immutable binding/readback; no stale cached membership authority. |
| `.agents` and `.claude` driving/board/init skills | .4 prepares gated work-unified-roadmap, drive-board/normal/best and check-board organization inspection. work-roadmap/pnext-item retain routine/native delivery; work-board/init/generated product bindings remain .5–.6. Both tracked skill roots and manifest projections are kept coherent; no current adoption claimed. |
| `NewSddWorkspace` / configuration / generated guidance | Product-specific owner/project/repository scope and portable commands belong to .5/.6 publication and installed adoption; generated defaults unchanged here. |
| `coord-board-reconcile.yml` | Manual credential-free legacy diagnostic, no board writes. |
| `coord-board-archive.yml` / `coord-board-archive.py` | Separate still-scheduled legacy Project 1 archive using its App. Retain exact scope. |
| `scripts/fsgg-coord` / shared metered transport | Default shared Projects route. The explicit user-authorized native one-shot exception below applies only to this .2 pilot; .3 retains production composition and qualification. |

Only the selected .2 pilot is closed. The .3 adapter still needs actual automatic-writer composition,
immutable item verification and production correspondence for its own wire/provenance contract. Its
wrong-project, foreign repository, incomplete/denied reads, stale source, field drift, lost response,
owner-edit preservation and population-policy qualification remain distinct. No .3 activation is
inferred from the private manual operator. No broader transfer, consumer switch, package publication
or product adoption occurred through this pilot. Telemetry is NOTCONFIG with no handle; usage and
economics remain Unknown.

## Restricted root-local refresh qualification — 2026-10-02

COORD-BOARD-V2-01.3 passed for the selected root-local Project 3 Observation route.
Protected source [3829d42d](https://github.com/FS-GG/.github/commit/3829d42dd668eac5de33ebaaafbedfb342ac74be),
tree `8cc6b44dc9c8185c26ba033f960a12a3d908147e`, delivered the fixed adapter and CLI after
40 projection/transport controls and four CLI controls passed in native CI. They retain denial,
stale or changed source, incomplete pagination, wrong scope, ambiguous response and replay refusals.

Root independently authenticated the clean Release artifact: 91 output files, nine official
NuGet archives, 49 package runtime assets, 97 real PDB source documents joined to exact Git bytes,
and 3,849 installed SDK/runtime/host files. Actual SDK/compiler was 10.0.401 and the CLI host was
.NET 10.0.12. The loaded adapter SHA-256 is
`7bdf188e6e7c82a256f8c185537914b34ef6fccf0bfddda595a218c79bb407cd`;
CLI SHA-256 is `50bff72b74fc6bcd1b87f52ac2ff597da01b5b669181a6681ade3ce8d98995f9`.
Compiler pseudo-documents are explicitly non-source; real source checksums remain required.

The binding preserves constructor source `47d0a38ca1f3b390bb20b88f04c8ba019d989c15`,
constructor SHA-256 `dac15107412e92e9238f232008137244019957b7b57ab164bcd58b647a5227d3`,
and population revision `351e5db40d4d72abd6e87882614725305eb2a419`.
The current manifest blob `f0de0955c30158272669d18e4ed3501b743e043a` was unchanged through
protected main `e9bee62a8d3d27bb765e751a65876946e8168940` at execution.
Its earlier inventory gaps remain a historical capture, not a new carryover approval.

At 16:17 UTC, the fixed CLI verified all three approved pilot items with zero writes. Root then
staged exactly one Observation=Unknown fixture after a complete independent project read.
The fixed CLI restored that item to Verified with exactly one acknowledged HTTP 200 mutation;
the response SHA-256 is `987a0c399d09ea4f0925dcd906f4c3b6e71dc6e731988ef6487958d6e1bfddbd`.
Independent readback and a repeat verified all three items; the repeat emitted zero mutations.
Complete field readback, including native labels and repository values, matched the pre-fixture
snapshot exactly. All human-owned fields and the fourth, unapproved SDD#935 membership were preserved.
Root live-proof receipt SHA-256: `a157b65cdaa14700fb6c33538de974ec7508f493242a0d3bc39d4153263e8b70`.
Private payloads and credential metadata remain private.

This qualifies the fixed root-local route. The hosted workflow remains dormant and unenrolled;
its credentials, executable binding and activation require their own verified join. No generic
production writer, retry authority, wider import or legacy-board write was enabled. Ordinary source
PR #4116 passed and merged while the board job remained unavailable. Broader consumer adoption,
carryover and scheduling changes remain .4; product publication and installed adoption remain .5–.6.


## Four-target successor and consumer adoption — source preparation

**COORD-BOARD-V2-01.4 remains open.** At source preparation, root authorized this finite source work
without live board changes. The subsequently selected root operation is recorded below. The successor is the original three plus existing
[.github#3009](https://github.com/FS-GG/.github/issues/3009), node
`I_kwDOS6feoM8AAAABOUU1ew`, open at revision `2026-08-26T20:18:16Z`.
It is appended as a new row, not inferred from a missing candidate. Proposed seed is Ready /
Active delivery / Verified, Roadmap `.github#3008`; root may retain Backlog while capacity is occupied.
These are initial-seed intentions, not observations of existing Project3 values. SDD#935 remains
unapproved, unseeded and outside both selected cohorts. No Governance or product scope is added.

Fresh native issue reads on2026-10-02 confirmed all four exact node IDs and unchanged open revisions.
Their REST blocked_by snapshots returned HTTP200, `[]`, ETags and no Link: .github3009 at18:18:58UTC,
SDD928 at18:21:29UTC, Templates441 and .github3010 at18:21:49UTC. The successor manifest retains
these complete native observations. Empty native dependencies do not clear body/owning-plan source,
publication or installed prerequisites. Current Project3 membership for3009 is **Unknown here**;
no absence, duplicate, field seed or current schema is inferred from an earlier three-item receipt.

### Architecture remaining-outcome adjudication

[Maintainer comment5430619257](https://github.com/FS-GG/.github/issues/3009#issuecomment-5430619257)
already accepts the ecosystem-neutral descriptor-driven catalog, typed prompts, identifier derivation,
capability bindings, skills, package-only qualification and producer-before-consumer publication.
It explicitly retains the organization ADR, linked design and implementation children as deliverables.
At protected `eecbae549802ac93b8241f887d29a590a61b9dd0`, source inspection found
[ADR0071](../adr/0071-two-web-workspace-providers-one-template-package.md),
[ADR0072](../adr/0072-console-and-fable-bindings-are-separate-workspace-providers.md),
[ADR0077](../adr/0077-quint-first-typed-specification-authority.md),
[architecture](../architecture.md) and the
[older polyglot web design](../reports/2026-07-27-163509-polyglot-web-product-architecture-and-implementation-design.md).
Those records establish narrower existing provider shapes/lifecycle authority. They do not supply the
complete four-provider descriptor/typed-prompt/governance/owner/compatibility contract requested by3009.
Carry forward that ADR/design delivery to the .github architecture/provider-contract owner; do not
ask the human to choose the already accepted architecture again or claim native acceptance.

The [bounded outcome dispositions](board-v2-outcome-dispositions.md) cover all37 actual named Unified§9.8
rows and explicit historical/native candidates. They keep unrepresented/native-identity gaps, public
artifact and native/human evidence gaps visible. They do not create37board rows or qualify a global
switch; unresolved identities and later approved imports remain work for the owning integrator.

### Prepared source and consumer route

`board-v2 inspect` uses the authoritative source collector and exact bound planning readers; its
`fsgg.coord.board-v2-inspection/1` output keeps native/dependency/plan evidence, human Status/Track/Roadmap,
Observation, source currentness and acceptance gaps separate. Population failure is Selected=Unknown,
not an empty backlog. It sends no mutation/retry, takes no writer lock, creates no verification timestamp
and does not schedule. Historical LastVerified is display only and cannot authorize future writes.
The [precise CLI example](../../tests/coord-board-v2-cli-join/INSPECT.md) states the command/wire contract.

The stateless Observation authorizer source admits only the exact original-three or exact proposed-four
native identity maps. Dispatch freshly checks the *selected binding's map*, not a union:3009 is refused
under an original-three binding,935 under both. Exact Project3/org/ObservationVerified/document/variables,
one HTTP dispatch and no retry remain enforced. This descriptor expansion grants no manifest, artifact,
credential or native operation authority; generic Unavailable fencing and legacy lifecycle stay intact.
No new durable state protocol or redundant Quint model is introduced.

Organization-only source guidance is prepared for work-unified-roadmap, drive-board/normal/best and
check-board, with one [shared inspection contract](https://github.com/FS-GG/.github/blob/main/.claude/skills/check-board/references/organization-v2-inspection.md).
The four newest-model user edits are preserved: newest available Astra/high and Sol/medium are resolved
at each new spawn. Both tracked skill roots remain coherent; no authored root is replaced by a generated
view. Product work-board, work-roadmap/pnext-item, initialization and template/provider/lifecycle defaults
keep their current authority. Legacy Project1 is the retained reference and its separate archive remains
scheduled. The hosted V2 writer remains `if:false`, unenrolled and unqualified for activation.

### Root-only qualification and adoption gate

1. Accept the coherent protected source and independently authenticate the actual constructor,
   adapter and CLI closure. Freeze a **new** successor population revision/blob and exact four-target
   private binding; original population351e and .3artifact/receipts remain historical identities.
   Changing the current canonical manifest invalidates the old binding's current-blob check until a
   selected rebind; never rewrite a historical binding/receipt or fallback to stale freshness.
2. Root takes fresh complete bounded Project3 membership/schema/all-fields and native issue/dependency
   snapshots. Pure constructor dry-run must show at most the one selected missing3009 membership and
   its four absent initial seeds, with zero intents for original3,935, native data or Project1.
   Conflict, bound, denial or ambiguous/late evidence stops. Apply only root's selected one-shot stage;
   delayed add visibility requires independent readback, never a blind retry. Repeat must emit zero intents.
3. Qualify the actual restricted four-target no-op/inspection and independent before/after readback;
   preserve all human/native fields and unselected935. Then root consumes the exact protected inspection
   and records one bounded dry-run scheduling selection (3009 only if actual plan/intake/PR/touch-set/capacity
   facts permit). No dispatch is required merely to prove selection.
4. Record the selected consumer invocation/adoption evidence here before treating this organization scope
   as its primary queue. Until then every prepared consumer route stays gated. V2 unavailable/stale evidence
   remains explicit Unknown, preserves current work and never becomes an ordinary source merge gate.
   All unswitched consumers and unresolved/unrepresented named outcomes remain reported; broader .4closure,
   .5publication and .6installed product adoption remain separate.

**Actual bounded successor qualification — 2026-10-03:**
[Public installed receiver and native evidence](board-v2-installed-qualification.md#actual-bounded-root-import-and-installed-four-target-qualification)
verifies 0.95.0 CLI/Kit/Drivers consumption and retained exact-bound constructor custody. Root selected
one add plus four absent seeds, independently read back each stage and made exactly five acknowledged
mutations without retry. .github#3009 is now item `PVTI_lADOEYAWY84Bldpazg-REU0`, Ready / Active delivery /
Verified with Roadmap .github#3008. Complete independent readback preserved original3, unselected
SDD#935, all native values and the schema. The explicit import repeat retained all 16 planning values
with zero intents. Actual installed inspection and restricted refresh each verified all four; refresh
made zero writes and complete fresh before/after snapshots were identical.

**Selected consumer adoption:** root selected the check-board read-only organization V2 consumer at
05:32:42 UTC and independently invoked the actual installed CLI at 05:32:52 UTC: exit0,
selected/attempted/verified4, zero mutations and no population gap. Root selection receipt SHA-256 is
`2bc924438d04915989018af5c5de9f352d056049c828d79464ccd12596eca2e6`; actual inspection SHA-256 is
`fbab2db499b3fd6ba525d6fda6179abb727ac62b99037c68698ba1a225189dc8`. At 05:35:54 UTC, root selected
only .github#3009 for a bounded prospective ADR/design dry-run using actual intake, remaining-outcome
adjudication, reviewed PR linkage, disjoint documentation touch sets and owner/PR capacity. Dry-run
receipt SHA-256 is `4bab632070107d12031fc6155ae2eda6f213f37a42662cc23dd0c0dbe06c9aec`.
Its architecture choice stays accepted; ADR/design delivery remains unverified. No worker was dispatched.

**Bounded window qualified; canonical activation awaits native evidence landing.** Once this owning
record lands, only the selected check-board four-target organization scope uses the bound inspection
as its primary queue. Other organization driving consumers, product boards, initialization and generated
bindings remain unswitched. A missing/stale V2 read reports Unknown, preserves owning roadmap work
and does not become an ordinary source merge gate.
Broader unresolved/unrepresented carryover keeps .4 open; .5–.6 remain separate. No native issue
lifecycle/dependency change, permission/credential change, hosted-workflow activation or legacy write
was performed. Telemetry NOTCONFIG/no handle; native usage/economics Unknown.


## Work-unified-roadmap selected consumer — 2026-10-04

Root selected and accepted the existing installed `0.95.0` four-target organization inspection
for `work-unified-roadmap`. The [actual installed consumer evidence](board-v2-installed-qualification.md#work-unified-roadmap-consumer-acceptance--2026-10-04)
records the fresh report, executable/binding custody, zero mutations and bounded no-selection dry-run.
The admitted scope remains SDD#928, Templates#441, `.github`#3010 and `.github`#3009; SDD#935 is
excluded. This canonical follow-up records that selected consumer after native landing. The earlier
check-board scope stays adopted; `drive-board`/normal/best, product boards and broader carryover
remain separate. Full .4 stays open under the [current outcome dispositions](board-v2-outcome-dispositions.md#bounded-outcome-reconciliation--2026-10-04).


## Three-active successor reconciliation — 2026-10-07

Root selected a new reviewed active population: SDD#928, Templates#441 and .github#3010.
Fresh authenticated four-issue reads and complete Project 3 schema/membership/all-planning-field
connections retain five native memberships. All three active native dependency snapshots are
complete and empty. Their human Blocked values and body/owning-plan producer publication and
installed acceptance prerequisites remain. This source selection grants no binding or native effect.

.github#3009 is closed completed at observed revision `2026-10-04T13:04:38Z`; [PR4192](https://github.com/FS-GG/.github/pull/4192),
protected `4fc6edf6acce60760cea858ba15e4384c9ce0340`, delivered ADR-0092 and its linked design.
The manifest retains its canonical identity as `omit-delivered`, closed, `pilot=false`,
`remainingOutcome=null`, `adjudication=unknown`. The legacy adjudication enum records remaining-work
admission; unknown here does not dispute the delivered architecture. Its Ready/Track/Roadmap seed
values remain historical, with no fabricated Done or project-field write. Its membership and SDD#935's
unapproved membership remain untouched. No other issue is selected.

The historical four-target population remains [blob b8e99000](https://github.com/FS-GG/.github/blob/65c8d8ced152cea2634e44521cf25a572c26f79c/docs/coordination/board-v2-import-manifest.json),
Git blob `b8e99000690ba0136290b40b6a33e3a43cbc7f02`. Original inventory, pilotOperation,
operationProvenance, successorPreparation and accepted receipt identities are unchanged dated history.
The original private binding SHA-256 `68375c1cd8efc64d5b04534a6baa5e6658daa257f14f33ca2e5f1e158cb5c320`
remains NOT LOCATED in its recorded search scope; no reconstruction is permitted.

After source delivery, root must independently authenticate the current protected blob, installed
CLI/adapter closure, exact project/schema/native identity and genuinely qualified constructor provenance.
It then separately admits a DISTINCT version-2 binding selecting exactly these three active nodes and
one bounded actual installed `board-v2 inspect` invocation. Changing this manifest intentionally invalidates
old current-blob correspondence: historical four-target and original-three receipts cannot qualify it.
Acceptance needs selected/attempted three, all Current, complete dependency and plan coverage, no
population/planning gaps, truthful health, zero mutations and root's exact consumer adoption decision.
Human Blocked remains Blocked. Closed #3009 is authenticated retained history, not a fourth Current row.
The OPEN/current-revision guard stays strict. Full .4, other consumers and product adoption remain open.


## Three-active operation and selected consumers accepted — 2026-10-10

The [actual installed qualification](board-v2-installed-qualification.md#three-active-installed-inspection-and-consumer-acceptance--2026-10-10)
now satisfies the Oct7 gate for the DISTINCT three-active binding. Root accepted the current
programme organization-board inspection and check-board no-dispatch consumers at 04:43:23 UTC.
CLI0.100.0 reported exactly **3/3/3 Current/Verified**, complete dependencies/owning-plan reads,
no gaps and zero mutations. Complete before/after native data retained all five memberships and
schema/field values: SDD928/Templates441/.github3010 remain Blocked, closed3009 remains Ready,
and935 remains unapproved/unseeded. No import, refresh, field normalization or legacy fallback ran.

The first before-census failure, with installed inspection unattempted, remains distinct from the
accepted fresh successor. The linked evidence records actual binding/report/root acceptance hashes,
byte custody and cleanup limits. Unknown candidate acceptance/intake/PR/touch-set/capacity stays
Unknown. This bounded consumer decision does not finish remaining approved carryover or switch
`drive-board`/wrappers/product boards; full .4 stays open, .5 publication complete, .6 separate.


## Canonical drive-board consumer accepted — 2026-10-10

Root's [actual no-dispatch adoption](board-v2-installed-qualification.md#canonical-drive-board-no-dispatch-adoption--2026-10-10)
at 05:20:25 UTC consumed the exact three-active binding/report and canonical source `3c53055f`.
It selected no work, retained all three human Blocked values and preserved Unknown candidate
acceptance/intake/PR/touch-set/capacity. This closes only the canonical driver consumer gate;
normal/best wrappers remain unswitched. There was no new inspection or native mutation, and report
freshness remains 04:41. Source delivery is established separately by this change’s native merge readback. Remaining
carryover and unselected consumers keep .4 open; .5 is complete and .6 remains separate.


## Wrapper consumer acceptance — 2026-10-10

Root accepted [both wrapper no-dispatch consumers](board-v2-installed-qualification.md#normal-and-best-driver-no-dispatch-adoption--2026-10-10)
at 06:24:36 UTC against canonical source `5be234a3` and the complete retained three-active report.
No work or worker was selected; wrapper/model bytes stay unchanged. Normal Codex
`gpt-5.6-terra`/`medium` remains unavailable and refuses actual dispatch; best availability grants
no dispatch authority. All three stay human Blocked, #3009 stays Closed/completed with Ready
retained and #935 unapproved/unseeded. Candidate acceptance/intake/PR/touch-set/capacity stays
Unknown, with original 04:41 report freshness and no new inspection or native effects.
Source delivery awaits this follow-on's native merge readback. Remaining approved carryover,
unselected consumers and native legacy-reference evidence keep .4 open; .5 complete and .6 separate.


## Native legacy-reference gate accepted — 2026-10-10

The [actual Project 1 metadata operation](board-v2-installed-qualification.md#project-1-native-legacy-reference-notice--2026-10-10)
marked the exact retained board as legacy at 06:57:17 UTC, with full original text preserved and
independent readback. Title, open state and identity stayed unchanged; no item, membership, field,
archive, closure or issue-body operation ran. The retired lifecycle writer and separate historical
archive/bootstrap boundaries are unchanged. This supersedes only the pending native notice gate;
remaining approved carryover and unselected outcome/consumer authority keep .4 open. No additional
identity, including SDD#935, is selected. Publication .5 remains complete and .6 separate.

## Batched programme successor preparation — offline planner ([#4941](https://github.com/FS-GG/.github/issues/4941), PR 1)

`tools/BoardV2Import` gains four offline subcommands that replace the per-cohort cloned wrapper scripts. The
decision logic is the pure planner `FS.GG.Coord.GitHub/V2Enrollment`; the subcommands only read files, call it
and write outputs with the same exclusive `CreateNew` 0600 custody, directory fsync and readback as
`prepare-programme`. None performs a native read or write, and none grants enrollment, admission or
implementation authority.

| Subcommand | Reads | Writes |
|---|---|---|
| `plan-successor --request --native-facts --repository --template-output --payload-output` | closed request, read-only native facts, predecessor private pair, protected public blob at the merge sha | successor binding template and payload (private custody) |
| `prepare-successor --request --template --payload --private-output --public-output --receipt-output` | request, template, payload, predecessor private pair | the successor pair and a closed non-secret `receipt.json` |
| `bind-runtime --private-binding --repository --merge-sha --runtime-output` | successor private binding, public blob at the merge | runtime binding differing only in the 40 hex bytes of `populationRevision` |
| `render-carrier --receipt --output` | `receipt.json` only | fixed-template carrier document |

The successor payload is the predecessor payload bytes before its closing `]}`, a comma, the appended rows and the
closing `]}`: a raw-byte splice, never a re-serialization, so the prefix is exact by construction and is verified
again independently after the pair exists (`prefixPreserved`, `headerPreserved`, `recomputedCommitment`). A
Roadmap is generated only from `programmeIssue3008` or `protectedDoc{repo,path}` and must pass the installed
reader's own plan grammar (`V2ProjectionSource.programmePlanBlobGrammar`, now shared by the reader), which refuses an
Outcome issue URL before anything is constructed. The programme row ceiling is one value, `V2Projection.maximumProgrammeRows`, shared by the reader and the planner (see "Row ceiling" below).

Refusals print one closed code on stderr and never payload, nonce, path or input text: `DuplicateIssue`,
`AlreadyEnrolled`, `IsPullRequest`, `NodeIdMismatch`, `NotProjectMember`, `RepositoryOutsideBinding`,
`PredecessorCommitmentMismatch`, `PrefixNotPreserved`, `PlanNotAdmitted`, `PlanBlobAbsentOnMain`, `RowCapExceeded`,
`ConflictingHumanValue`, `PartialVisibility`, `OutputExists`, `MalformedInput` and, split by cause (PR 2),
`InputUnreadable` (an input file is absent, linked, oversize or unreadable), `CustodyUnavailable` (an output could not be
created, synced or read back), `GitReadFailed` (the protected public blob could not be read through git; a blob that is read
and does not authenticate stays `PredecessorCommitmentMismatch`), `NativeReadFailed` (a read-only GET failed or returned an
unusable body) and `SuccessorNotRederived`. A `CreateNew` that loses a race is `OutputExists`, not a custody failure.

## Supported-route preflight and online resolution ([#4941](https://github.com/FS-GG/.github/issues/4941), PR 2)

`prepare-successor` now takes `--native-facts` and `--repository` as well and re-derives the successor from the request,
the predecessor and the facts before the constructor sees anything. The supplied template and payload must be exactly
that: a hand-edited appended Roadmap that the installed constructor would accept refuses as `PlanNotAdmitted`, a node id the
facts do not support as `NodeIdMismatch`, a changed prefix as `PrefixNotPreserved` and any other difference as
`SuccessorNotRederived`, all before any output exists.

`resolve-issues --request --native-facts-output` is the online half of `plan-successor`. It is read-only by construction:
the only effect is a `GET` through the GitHub CLI with a fixed argument vector (`--method GET`, never a field, input or
GraphQL endpoint), and the path must be exactly one of ten allowlisted endpoints: the Project, its fields and its items; a
repository, one issue, one pull request and its commits, the open pull requests and the `main` ref of a repository; and a
document on `main`.
A path segment is a name (`[A-Za-z0-9_.-]`, no `%`); only a whole `.` or `..` segment is a traversal, so a name like `a..b.md`
is reachable. It reads the Project, its fields and every item, each request issue and each protected plan document on
`main`, and writes the `fsgg.board-v2-native-facts/1` document the planner consumes. A clean
404 on an issue is reported as partial visibility and refused by the planner; any other failed read, or a response over the
32 MiB stream cap (the process is stopped, never buffered whole), is `NativeReadFailed` and writes nothing. A plan document
that reads 404 is an absence only if the repository itself can be read; otherwise the repository is reported as hidden
(outside the fixed facts document) and the preflight says `Unknown`, not `Unsupported`.

`preflight --route --stage --request --repository (--online | --native-facts)` returns a typed verdict per registered check
for the next stage of a closed route: `SupportedForNextStage` (with evidence), `Unsupported` (an exact reason and who can
supply what is missing) or `Unknown` (an exact gap). The exit code is 0 only when every check is supported, 10 when any is
unsupported and 11 when none is unsupported but any is unknown; unknown is never 0, and 2 stays a refusal to run. Registered
routes and their stages: `ProgrammeEnrollmentSuccessor` (`PlanSuccessor`, `PrepareSuccessor`, `BindRuntime`),
`RoutineDelivery` (`OpenPullRequest`, `MergePullRequest`) and `PublicationWriterRegistration` (`RegisterWriter`,
`RunWriter`). A route and stage that do not belong together report `Unknown` (`Registry/NotRegistered`), never supported.

`BindRuntime` observes its own inputs (`--successor-private`, `--successor-merge-sha`, and the public blob at that merge read
through `--repository`): the successor pair must authenticate against that blob (`SuccessorIntegrity`), extend the predecessor
(`SuccessorExtendsPredecessor`), and carry plan references the installed grammar admits and that exist on `main` now, judged
from the successor's own appended rows (a sha-pinned reference is immutable and says nothing about `main` now, so it is `Unknown`, not supported). Missing successor inputs are `Unknown`, never taken from the predecessor.

| Check | Decided from | Unsupported when | Unknown when |
|---|---|---|---|
| `ConstructorIdentity` | running constructor source revision, intended protected main (`--online`: the live head by GET; `--main-revision`: asserted; otherwise the local tracking ref, which may be stale) | they differ from a live or asserted main | either unreadable, or the main is only the local tracking ref: a stale checkout can equal a stale ref, so a match proves nothing offline and the exit is never 0 |
| `PredecessorIntegrity` | private pair, public blob at the recorded merge | the commitment differs, or the payload cannot be spliced | either unreadable |
| `RuntimeBindingIntegrity` | runtime binding against the private pair | it differs in anything but `populationRevision`, or that is not the recorded merge | not supplied |
| `FactsComplete`, `IssueKind`, `IssueIdentity`, `ProjectMembership`, `RepositoryInBinding`, `NotAlreadyEnrolled`, `RoadmapConflict` | the planner's own per-row findings, all of them rather than the first | a row fails the check (the row is named) | facts partial or unobserved |
| `PlanGrammar` | the installed plan grammar; the installed artifact sha against a known table | a plan reference is not admitted | the installed artifact is not in the known table |
| `PlanBlob` | plan documents on `main` now | a document is absent in a readable repository | facts were supplied rather than read now, or the repository may be invisible (`PlanBlob/NotVisible`) |
| `RowCap` | predecessor rows plus request rows against 100 (readable by every reader), 250 (readable only by a reader named in `--known-lifted-cap-readers` or `--known-cap-500-readers`) and 500 (readable only by a reader named in `--known-cap-500-readers`) | the total exceeds 500 | above 100: the installed reader is unnamed, not in the applicable table, or that table is not supplied; or the predecessor is unreadable |
| `PayloadBound`, `TemplateBound` | computed payload and private binding sizes against the installed reader bounds | either is exceeded | inputs unreadable |
| `ToolchainLock` | locked FSharp.Core `contentHash` against the cache's `.nupkg.metadata` | they differ (the NU1403 risk) | either unreadable |
| `MemoryHeadroom` | smallest remaining bytes over the process cgroup chain, and host `MemAvailable` (the smaller of the two; alone when no cgroup limit applies) | below the minimum | cgroup unreadable, or `MemAvailable` unreadable (a known cgroup limit does not bound the host) |
| `ProjectAutoAdd` | Project items not in the binding's enrolled set; every enrolled node must be in the inventory | never: no breaking condition is proven | any lacks Status, Track or Work level (each named with its missing fields), an enrolled node is absent from the inventory, or items are unobserved |
| `AdjacencyChurn` (`BindRuntime`) | unrelated item `updated_at` within the window | never | any unrelated update in the window |

`ProjectAutoAdd` is never `Unsupported`: on 2026-10-30 the installed caller kept working with a bare unenrolled membership (and three legacy partial ones) present, so that such memberships break it is unproven; only a sudden rise in them matched the later refusal, whose code was coarse. They are reported as `Unknown` with each item and its missing fields, and the JSON finding carries a note saying so. No `Unsupported` verdict exists for a condition that is not proven.

`RoutineDelivery` checks (`OpenPullRequest` on a candidate that is not yet a pull request, from the checkout's branch and head with `--title` and `--body-file` and the commits between `refs/remotes/origin/main` and `HEAD`; `MergePullRequest` on `--pr N`, with its commits read by GET). The marker, operation, changed-path and closing-keyword grammars are **not re-derived**: the preflight runs the maintained gates from the checkout and observes their results. `scripts/check-claim-generation.py` (a routine head, `--base-sha`, `--routine-policy-ref`) decides `MarkerSingle` (exactly one complete marker, no repeated field), `MarkerHeadBound`, `MarkerOperationAllowed` and `ChangesPresent`, in the gate's own order: a finding decides its check, earlier checks passed, later ones were not reached (`RoutineGate/NotEvaluated`); a gate that cannot run (rc other than a finding, a base the checkout does not hold) is `Unknown`. `scripts/check-closing-keywords.py` (title, body and commit messages) decides `ClosingLinkDeclared`: rc 1 (a closing keyword that is not declared on a line of its own) is `Unsupported`, rc 0 with a declared closing line is `Unknown` (whether it fulfils the issue is not decidable here), rc 0 with none is supported, and rc 3 or any failure is `Unknown`. `RoutineBranch` uses the gate's own pattern `^routine/[a-z0-9][a-z0-9._-]*$` and the gate is not run for a branch outside it. `SinglePullRequest` needs none open before opening and exactly one before merging. At merge only: `BaseIsDefaultBranch` and `PullRequestOpen` (at open there is no pull request, so both would be tautologies) and `BaseCurrent` (a recorded base that is not the live default branch head is `Unknown`, because that is the `routine-eligibility` race).

`PublicationWriterRegistration` checks: `RegisterWriter` needs a readable workflow that carries a publication effect token (comment lines excluded) and is neither a registered writer nor exempt in this repository; `RunWriter` needs it to be a registered writer and the maintained `scripts/check-publication-writer-coverage.py --local` to report zero unmet. That script stays the authority: its result is observed, not re-derived.

The toolchain check compares the lock file with `.nupkg.metadata`, not with the cache's `.nupkg.sha512`, which is a
different digest of the same package and would report a healthy cache as mismatched. The known-reader table is supplied
(`--known-readers`, one sha256 per line) and is empty by default, so the installed grammar is `Unknown` until a verified
installed artifact is listed. Without `--online` or `--main-revision` the intended main is the checkout's local `refs/remotes/origin/main`, which may be stale. The preflight grants no authority, performs no mutation and prints no payload, nonce, private
binding content or content node id (`I_...`); it prints issue references, and the Project item id (`PVTI_...`) only for a
non-issue item, which has no reference. Engine registration and lifting the row cap remain later deliveries of the same issue.

## Row ceiling, gate trust and the engine surface ([#4941](https://github.com/FS-GG/.github/issues/4941), PR 4)

**Row ceiling.** The programme population ceiling was 100 rows and is now 250, as the coordinated reader and engine release the
design called for. It is one constant, `V2Projection.maximumProgrammeRows`, enforced by the reader for both the private payload
rows and the binding's selected issues, and used by the planner and the preflight, so the three cannot disagree. The byte bounds
(128 KiB payload, 256 KiB binding) are unchanged and still refuse fat rows long before 250 (the current 93 rows are about 17 KiB).
A reader older than this release refuses more than 100 rows, so the preflight treats 101 to 250 as readable only by a reader named
in the supplied `--known-lifted-cap-readers` table (empty by default: `Unknown`), and treats more than 250 as unsupported. The
choice of 250 is a bound chosen for headroom, not derived from the design; it is a single constant if Root wants another.

**500 rows and the scaled request ceiling ([#5119](https://github.com/FS-GG/.github/issues/5119)).** The user limited the
Coordination board and its readers to 500 items, so `maximumProgrammeRows` is now 500. Coherent 0.114.0 readers refuse more than
250 (`previousProgrammeRowCap`), so the preflight keeps two tables. 101 to 250 rows need a reader in either
`--known-lifted-cap-readers` or `--known-cap-500-readers`. 251 to 500 rows need a reader in `--known-cap-500-readers`, so a stale
250-row table can never admit them. More than 500 rows are unsupported. The byte bounds are unchanged. The cohort-90 binding measured
37,938 payload bytes and 62,651 binding bytes at 207 rows, which projects to about 92 KiB and 150 KiB at 500.

The CLI's bounded transport, shared by `inspect` and `refresh`, had a flat 500-send ceiling and a 270-second deadline. The
207-row cohort-90 inspection exhausted it, with 2,502 reads and all 207 rows `Unknown`. Three changes cut the inspection reads
per row:
- A programme binding reads each issue's blockers in the native observation GraphQL query (`blockedBy`, with the REST reader's checks), not a separate REST `dependencies/blocked_by` read. That read was two unbatched sends per row.
- A binding-5 successor reads Work level in its planning membership query, not a separate `selected work classification` read.
- Inspection batches alias 25 reads per send instead of 10.

The fixture inspection now takes 81 sends at 207 rows and 175 at 500, down from 635 and 1,517. The live read-only run of the
207-row binding was retained on 2026-10-31 at 18:31Z. It completed in 120.1 seconds for 87 GraphQL points and 19 REST reads, with
205 of 207 sources Current, no membership or population gap, and zero mutation attempts. The private run directory and its
counts-only summary are kept by the #5119 owner. The ceiling is now `V2Projection.refreshRequestCeiling`: 64 sends plus 2 per
selected row, never below 500 (1,064 at 500 rows). The deadline is `V2Projection.refreshDeadlineSeconds`: 60 seconds plus 0.7 per
selected row, never below 270 (410 at 500 rows).

Both values are derived from inspect measurements only. `refresh` is not batched: it takes about 6 sends per row in the fixture
(1,243 at 207 rows), so it stops after about 80 rows inside this budget. Batching it, or refusing larger bindings before any send,
is [#5131](https://github.com/FS-GG/.github/issues/5131). The
complete-membership read refuses a board whose `totalCount` exceeds `maximumBoardItems` (500) on its first page, rather than paging
up to the limit.

**Gate trust.** The routine gates (`check-claim-generation.py` with `lib/gate.py`, and `check-closing-keywords.py`) run from the
exact base commit's blobs (`git show <base>:<path>`), the way `routine-eligibility.yml` does, never from the checkout, so a pull
request that edits a gate cannot predict its own pass; if the base does not hold them the result is `Unknown`. The publication
coverage gate audits the checkout's own workflows from its own location and cannot be run from base blobs, so it is observed only
when the checkout's gate bytes equal the trusted base's (`refs/remotes/origin/main`) and is otherwise `Unknown`
(`CoverageAudit/GateModified`, or `/Unreadable` with no trusted ref).

**Engine surface.** The commands are registered in the coordination engine as the roots `enrollment` (`plan`, `prepare`,
`bind-runtime`, `render-carrier`, `resolve-issues`) and `route-preflight`, reaching the installed tool through the `fsgg-coord` shim, which execs the engine (`fsgg-coord-engine enrollment ...` and
`fsgg-coord-engine route-preflight ...`). Both are dispatch arms ahead of the typed handler table, like `context-view`, and are
not in the command catalogue; the V1 writer census classifies both in `outOfCatalogueRoots` as `writes: never` (they create only new owner-only local files, read GitHub by `GET`, and run the base-blob gates as subprocesses).
`preflight` is not a root because the engine already answers a skill `preflight` root. The constructor identity in a receipt is now
the `FS.GG.Coord.GitHub` assembly (revision and sha256) that both surfaces share, not the `BoardV2Import` executable.
