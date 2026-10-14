# FS.GG project and Atlas audit — 2026-10-14

The audit reconciles FS.GG project knowledge against the 24 organization repositories
and the externally rostered `EHotwagner/S.I.R.` repository. It updates Atlas as the
primary authoritative store of curated project knowledge and strengthens retrieval,
capture, handoff and persistent-memory guidance.

## Scope and method

The review refreshed repository visibility and default branches, inspected project
and file inventories, and followed selected entry points through implementation,
contracts, tests, package dependencies, workflows and owning acceptance records.
It distinguishes source implementation, publication, installed receiver evidence
and current operational authority. Historical receipts were inspected at their
recorded scope; this audit did not rerun every product or reproduce native gameplay.

All 38 unique citations in the original 75-record archive matched their immutable
Git bytes. Semantic review still found inaccurate summaries and stale navigation:
a matching digest authenticates cited content, not the interpretation of it.
Private source interiors, native receipts and detailed private findings remain in
an access-restricted archive and are excluded from the public corpus and seeds.

The repository roster declares participation in shared infrastructure. It is not
an organization census. The external S.I.R. repository and independent package
owners retain their own product, planning and delivery responsibilities.

## Repository coverage

Revisions below are the public source snapshots inspected during the audit. Private
rows deliberately expose only the existing public owner-plan boundary.

| Repository | Inspected public revision | Review focus |
| --- | --- | --- |
| FS-GG/.github | [14a93113](https://github.com/FS-GG/.github/tree/14a9311393b80906548595c2373b55aaa6cb7f4d) | Contracts, architecture, skills, delivery and agent guidance |
| FS-GG/FS.GG.Audio | [097e1feb](https://github.com/FS-GG/FS.GG.Audio/tree/097e1febe9f4dff8ca63d7571ef3bbff79822d2d) | Requests versus device effects, browser policy, thread and clock ownership |
| FS-GG/FS.GG.Coordination | [05e38c5b](https://github.com/FS-GG/FS.GG.Coordination/tree/05e38c5be9b8f7edde87cd9b94974f10a63b759b) | CLI/App separation, qualification, test replay, publication and activation |
| FS-GG/FS.GG.Coordination.Authority | [e1a9e315](https://github.com/FS-GG/FS.GG.Coordination.Authority/tree/e1a9e315f86e065c38d12b9f92e251d89479a77c) | Journal source, protected writer ownership and policy scope |
| FS-GG/FS.GG.Coordination.Authority.Sandbox | [fe6292e9](https://github.com/FS-GG/FS.GG.Coordination.Authority.Sandbox/tree/fe6292e9fa729a57cebbe4c8cf4d51dcad05500c) | Disposable qualification and retirement boundary |
| FS-GG/FS.GG.FourD | Restricted source reviewed | Public owner-plan boundary only; detailed findings retained privately |
| FS-GG/FS.GG.Game | [4163448c](https://github.com/FS-GG/FS.GG.Game/tree/4163448cfc69d428e136853212765690265b92c6) | Simulation, host effects, portable sources, WASM and refusal semantics |
| FS-GG/FS.GG.GitHub.Substrate.Sandbox | Restricted source reviewed | Public owner-plan boundary only; detailed findings retained privately |
| FS-GG/FS.GG.Governance | [b89a1251](https://github.com/FS-GG/FS.GG.Governance/tree/b89a1251325b6b75ad0ecfc8486663ce8a2b09be) | Kernel, effect hosts, provider binding, legacy CLI and enforcement gaps |
| FS-GG/FS.GG.Net | [5440ea46](https://github.com/FS-GG/FS.GG.Net/tree/5440ea46bfc3d63e77da2cbb778150a182dc94d1) | Transport/codec boundaries, delivery policy and channel termination |
| FS-GG/FS.GG.Rendering | [d73e17ba](https://github.com/FS-GG/FS.GG.Rendering/tree/d73e17ba529ef22f0d5155bdceac8ece68aeb3ef) | Portable Scene, native/browser adapters, presentation and version axes |
| FS-GG/FS.GG.SC2.Client | Restricted source reviewed | Public owner-plan boundary only; detailed findings retained privately |
| FS-GG/FS.GG.SDD | [cec249fe](https://github.com/FS-GG/FS.GG.SDD/tree/cec249fe6d4aa3dac1722999bb094fa3998ac70f) | Lifecycle dispatch, typed source, Knowledge API, provider/skill ownership and release set |
| FS-GG/FS.GG.Templates | [5acf3bba](https://github.com/FS-GG/FS.GG.Templates/tree/5acf3bba234595fc6404545777c687140b4390be) | ProviderTool, template defaults, publication and clean/retained receivers |
| FS-GG/FS.GG.Unity.Client | Restricted source reviewed | Public owner-plan boundary only; detailed findings retained privately |
| FS-GG/FSBarV2 | [70addcc1](https://github.com/FS-GG/FSBarV2/tree/70addcc11e356d8ab4b59b387d5bc3d363e213af) | Broker/browser/native separation, shared WASM, source versus native acceptance |
| FS-GG/FsQuint | [48cf3075](https://github.com/FS-GG/FsQuint/tree/48cf3075914583f9830e49a2f400678f1546024d) | Generic replay, optional tool wrapper, cleanup and package identity |
| FS-GG/HighBarV3 | [b57f11f2](https://github.com/FS-GG/HighBarV3/tree/b57f11f290dc3239d894b0285ae272b6b21bc25d) | Native plugin, engine-thread commands, role and ABI boundaries |
| FS-GG/RecoilEngine | [a35c9d8d](https://github.com/FS-GG/RecoilEngine/tree/a35c9d8d47eb81c19c6b87974ecba399f1c50206) | Inspected upstream fork versus selected pinned runtime input |
| FS-GG/specairn | No commit (empty repository) | Empty public reservation; proposed Host/forge design remains future work |
| FS-GG/svg-workspace-public-clean-20260914 | [f205975d](https://github.com/FS-GG/svg-workspace-public-clean-20260914/tree/f205975da486ac2c0e236a09e40ce73b6e529501) | Declared clean producer lineage and source-owned authority |
| FS-GG/svg-workspace-public-retained-20260914 | [f9d4ffb9](https://github.com/FS-GG/svg-workspace-public-retained-20260914/tree/f9d4ffb92c7cfac6b16034339ed030f95eba7d67) | Retained adoption provenance and preserved authored bytes |
| FS-GG/svg-workspace-qualification-clean-20260914 | Restricted source reviewed | Public owner-plan boundary only; detailed findings retained privately |
| FS-GG/svg-workspace-qualification-retained-20260914 | Restricted source reviewed | Public owner-plan boundary only; detailed findings retained privately |
| EHotwagner/S.I.R. | [80e1ac93](https://github.com/EHotwagner/S.I.R./tree/80e1ac9328865ec8d1ee3eeea130560ef22b1b01) | Externally owned product, project graph, current HTTP/SignalR protocol and skills |

## Findings and corrections

### Architecture and dependency descriptions

The architecture map no longer presents a selected component map as the complete
organization inventory. It identifies the compiled Templates ProviderTool, scopes
Rendering's dependency independence to its portable core, describes Kit
materialization instead of retired byte-copy synchronization, and replaces stale
Creator release prose with the owning receiver evidence.

Atlas's original [platform/dependency-direction](../atlas/records/be75cc1ee935f783be39978de91ae196c16141d4096e16eb6d07f59583296b4b.json) summary incorrectly equated
optional Governance enforcement with absence of a Governance.Config build dependency.
The cited architecture already contained that distinction. The corrected record
explicitly supersedes the inaccurate summary without rewriting its history.

### Source, published and consumed identities

Registry selections, producer source versions, served packages and installed
acceptance are separate evidence. Templates' registry selection remains distinct
from its accepted 0.19 public receiver journeys; clean installed WASM acceptance
is still a separate outcome. FsQuint's current source identity differs from its
accepted public release. Rendering deliberately uses a local-pack sentinel distinct
from its publication/profile version axis.

Coordination's test correspondence uses FsQuint, while its production qualification
project retains a separately locked SDD kernel path. The registry's consumer label
must not be read as proof that every runtime path has adopted the same producer.
Atlas records the actual consumer scope instead of declaring repository-wide absence
or completed installed migration.

The original WASM 0.3 installed producer boundary is accepted in newer shared
evidence. Selected current runtime source is byte-identical, but this does not
transfer that acceptance to a newly built archive or close product/native adoption.

Public package availability observed at 2026-10-14T11:45:02.817514+00:00. Each official index GET returned a parsed version list. This proves public version availability only; it does not prove exact original archive identity, GitHub-feed equality, installed adoption or runtime acceptance.

| Package | Source/selected version | Listed? | Latest listed stable | Official index |
|---|---|---|---|---|
| fs.gg.game.core | 0.17.0 | Yes | 0.17.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.game.core/index.json) |
| fs.gg.game.render | 0.17.0 | Yes | 0.17.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.game.render/index.json) |
| fs.gg.wasm.browser | 0.3.0 | Yes | 0.3.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.wasm.browser/index.json) |
| fs.gg.wasm.contracts | 0.3.0 | Yes | 0.3.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.wasm.contracts/index.json) |
| fs.gg.ui.scene | 0.32.2 | No | 0.32.1 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.ui.scene/index.json) |
| fs.gg.audio.core | 0.6.0 | Yes | 0.6.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.audio.core/index.json) |
| fs.gg.audio.webbrowser | 0.6.0 | Yes | 0.6.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.audio.webbrowser/index.json) |
| fs.gg.net.core | 0.6.0 | Yes | 0.6.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.net.core/index.json) |
| fs.gg.governance.config | 0.3.0 | Yes | 0.3.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.governance.config/index.json) |
| fs.gg.governance.cli | 1.12.1 | Yes | 1.12.1 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.governance.cli/index.json) |
| fs.gg.governance.referencegateset | 1.8.0 | No | 1.7.0 | [official index](https://api.nuget.org/v3-flatcontainer/fs.gg.governance.referencegateset/index.json) |

### Component questions requiring owner follow-up

Two public source-derived counterexamples deserve focused reproduction and resolution:

- [game/session-refusal-accumulator-gap](../atlas/records/48d094622ada1806cb30335926007c1abc476b1f7b45a2e250c490afa25e847b.json): failed elapsed-time advancement can retain
  a changed clock accumulator despite the public refusal-preserves-state contract.
- [net/multiplexed-terminal-admission-gap](../atlas/records/2591526ddabbea03fbdc38bf172bc54f42df9bc51eba7ad2b70d6e67215f08f1.json): a late multiplexed request can register
  after receive termination without the sequential path's terminal-admission guard.

These are open source-review findings, not newly executed CLR reproductions or fixes.
Their Atlas records identify the precise implementation, contract and existing test
seams. Component owners should resolve the intended behavior and qualify the affected
failure path before claiming closure.

Other records preserve device audibility, thread ownership, presentation-only
callbacks, generic replay cleanup and provider-execution boundaries. Automated
source checks, package references and synthetic fixtures do not establish those
separate operational outcomes.

### Product and fixture evidence

Public fixture records now distinguish declared clean producer lineage from a
preserving retained adoption. The retained fixture's four authored-file hashes were
checked against its committed provenance; the original package archives and adoption
receipts are not retained in that repository. Public summaries of private products
retain their owning-plan scope. Detailed private source findings remain private.

BAR, its native plugin and the inspected Recoil fork are reviewed as separate owners.
The default branch of an engine fork is not the selected game image input. Product
clocks, command permissions, loaded native artifact identity and actual gameplay
acceptance stay with their product gateways and owning evidence. The stock engine
and custom callback ABI are separate lineages. Factory production and rally queues
use different APIs; count-one production must not borrow ordinary unit-order SHIFT
semantics. Subscription snapshots and coordinator publication also use distinct
thread paths. Atlas captures these boundaries and the historical lessons about
float32 byte preservation, atomic process ownership and accidental artifact mutation.

## Keeping Atlas current

The canonical policy and generated agent guidance require Atlas retrieval before
substantial work and reconciliation at delivery. A changed finding is captured with
immutable citations and explicit supersession or conflict; unchanged knowledge needs
no ceremonial new record. An undelivered capture has a named gap and owner. Handoffs
and recovery retrieve stable Atlas IDs instead of repeating stale factual history.

Persistent memory carries repository/Atlas-ID pointers and retrieval instructions.
The working host's supported global Codex instructions now apply that rule to FS-GG
work across repositories. The public repository supplies the durable skill/policy;
private knowledge is selected explicitly and is never packaged into the public seed.

Archive discovery now finds the owning Git root from nested directories, including
Git worktrees. Guidance validation rejects reversed markers and preflights both
agent files before changing either. CI checks immutable history, canonical records,
seed correspondence, guidance/policy and actual SQLite retrieval behavior.

Generic SDD scaffolding still has a separate product Knowledge API and does not
consume `knowledgePolicy` as an automatic Atlas activation hook. Drivers policy
staging is transport; the delivered owner workflow and explicit guidance check are
the supported Atlas paths. Automatic generic scaffold activation needs an owner/provider
hook and installed-consumer verification. This source update and host configuration
do not assert a package publication or fleet-wide installation.

## Evidence entry points

These active Atlas records carry the immutable source citations behind the findings.
Use their stable IDs for later retrieval so explicit supersessions remain visible.

| Topic | Atlas record |
| --- | --- |
| Foundation and lifecycle | [sdd/project-knowledge-and-atlas-boundary](../atlas/records/d0df3068db679e24d9487ee09a6462ac5cf3df30f46ee466b66feb6dd5ff71e2.json) |
| Templates acceptance | [templates/0.19-publication-and-receiver-scope](../atlas/records/e7c9175fbfd9fd84bed95d552bc0a439b07a233c3e91fb39ae2f23197ee53f1b.json) |
| Generic replay | [fsquint/automata-qualified-and-deferred-scope](../atlas/records/a0ff0d0bb3a3c801b637d091293f2eeaac68e934274e4116d298ba60ec3461cc.json) |
| Component versions | [game/component-source-and-consumer-pin-snapshot](../atlas/records/cc24c3770587f2421514fa399d2617201752bdca145ac39058de91d45d3d19c1.json) |
| WASM producer acceptance | [game/wasm-supervisor-source-publication-boundary](../atlas/records/b25c41f19ba40e646fd9b6f82051ee4c38985539e445b26bd628eae98537df53.json) |
| Coordination consumer scope | [coordination/fsquint-test-production-joins](../atlas/records/69edc4285578313d1dd2f914d69add075a7b354f7fba717606d1abe15292576b.json) |
| Coordination execution boundaries | [coordination/ordinary-cli-execution-pair-publication](../atlas/records/d6077d547aa21ea10a9bdf6c752b8d1a0a6382b4b43a0fdf790f245e66a609fc.json) |
| BAR native lineage | [bar/stock-engine-and-fork-lineages](../atlas/records/8629fddeb4f273b35df69d4d28923adc14eff59d919b1a495d1a68818137c561.json) |
| Public fixture provenance | [svg-fixtures/public-retained-preserving-adoption](../atlas/records/b0ecfca0d114bae34ffa62ac3ea05159a4415fc6eb73ab1fcc4d79f657268f19.json) |
| S.I.R. product boundaries | [sir/actual-project-and-package-boundaries](../atlas/records/ba7d7464da8e13722232c8f86273774054693681becc450a68aea21a5a0e9b91.json) |
| Knowledge and memory authority | [atlas/storage-authority](../atlas/records/800060d7b4934508d0277f90a464e3112fd2fc99af4ef23825d09b0ff53f91c3.json) |

## Validation and follow-up

The audit adds 61 public record versions to the original 75, preserving every
original byte: 136 public records in total. Seven detailed private records remain
in the restricted local archive with separate local Git history and no remote.
The public seed contains no private-source interiors.

The audit updates curated knowledge and guidance, with the original archive history
preserved. Final archive, citation, privacy, mirrored-source and package-staging
checks are recorded with the accompanying PR. Product defect questions and remaining
installed/native gates retain their owning repositories and plans; this report is an
audit snapshot, not a second mutable roadmap.
