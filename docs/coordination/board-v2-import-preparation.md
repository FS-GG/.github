# Coordination V2 bounded import preparation

Retained preparation for **COORD-BOARD-V2-01.2**, refreshed 2026-10-02 from protected
`.github` revision `e6a7268ce92e7c6259a3872a12ac341708aeb222`, current native issue REST
readback and root's project readback. The [canonical plan](2026-09-29-coordination-v2-board-design.md)
owns acceptance. This source preparation cannot close .2: actual exact target/schema creation or
selection, an approved three-to-five issue pilot and independent native readback remain pending.

The [manifest](board-v2-import-manifest.json) contains **nine existing native issue identities,
zero approved additions, zero pilot items and seven adjudication gaps**. Its valid inventory is
non-executable. No issue was created, closed, reopened or converted from a PR. Retain legacy Project 1,
its history, lifecycle semantics and separately scoped archive operation.

## Current target capability

Root's 2026-10-02 12:13 GraphQL read found organization `O_kgDOEYAWYw`, legacy Project 1
`PVT_kwDOEYAWY84Bb08W`, and one inaccessible/null project node with `FORBIDDEN`. Although the
connection reported its final page, visibility is incomplete. **Coordination V2 existence and write
capability are Unknown.** This is neither proof of absence nor authorization to create a duplicate.
There is no title-discovery write fallback or Project 1 substitution. No credentials were acquired.
Project number/node ID and all new field/option IDs stay null until complete root readback.

## Current bounded inventory

All nine identities were read again through repository issue REST on 2026-10-02; node IDs and
`updated_at` revisions remain in the manifest. An open issue proves native state, not relevant scope.

| Identity | Current decision | Evidence and remaining gap |
|---|---|---|
| `.github#2954` | Omit superseded | ADR-0091 supersedes unfinished V1 migration ceremony. |
| `.github#2963` | Omit delivered at selected profile | Current protected V2 roadmap records full selected acceptance and clean-start operation complete; historical GS2-09–14 unchecked units are superseded. COORD-BOARD follow-on remains distinct. |
| `.github#2994` | Adjudicate | Historical epic explicitly says non-schedulable; current consumer/retirement/default scope needs owning-plan reconciliation. |
| `.github#2995` | Adjudicate | Native Q6 identity is open. Actual current published Quint and registry/workspace adoption must be reconciled; obsolete Q5/GS2 qualification cannot block it by inference. |
| `.github#2996` | Adjudicate | Retirement and soak scope requires current selected adoption evidence. |
| `.github#2997` | Adjudicate | Default decision remains separate; selection and current authority are unverified. |
| `Coordination#24` | Adjudicate | Its open read-only projection request references historical GS2/Q10 gates. Current delivered projection and any genuinely remaining scope must be reconciled. |
| `Templates#438` | Adjudicate; old source request superseded | Current `templates/fs-gg-fable-game/Directory.Build.props` blob `05807043215370cd2e47c9a0198c7ddfd48d6069` pins Game 0.16.0. Domain project blob `ea807de0db74443d83911dd12648a3f61a6f18f0` consumes that axis. This exceeds the requested 0.13→0.14 source change; historical feed/generated acceptance was not requalified here. |
| `.github#3010` | Adjudicate | Open catalog adoption identity requires actual current catalog publication and installed receiver evidence; `Templates#441` dependency remains Unknown. |

Source, publication and native acceptance are distinct facts within each outcome. Historical dependency
references are retained as evidence hints, never revived as current blockers or native dependency proof.
`Observation=Unknown` truthfully records missing current acceptance/adjudication even where REST state
is observed.

The manifest also lists current representative outcome slots without fabricating issue mappings:
SC2C-01.6f / OPS-TYPED-01.5 source repair, WASM-SHARED-01.4 publication, BARC-01.5 useful play,
LEARN-01.4 inactive C2 native qualification, COORD-BOARD-V2-01.2–.6, V2-LANG-01 publication/adoption,
and explicitly selected installed-host/W6/player-study follow-up. Active workers/branches retain their
own work. Merged Game #674 and SC2 PR #37 are delivery evidence, not remaining native issue anchors.
Planner's bounded open-issue inventory found no non-PR open issues for FSBarV2/SC2 and only Game's
Dependency Dashboard. Its first-page scope is not a historical census. No three-to-five admissible cohort
has been established. Routine source work continues without board membership.

## Consumer inventory and ownership

| Consumer | Current binding and disposition |
|---|---|
| `Board.fs/.fsi` | `exact-project1` stays pinned to legacy organization/project identity. Preserve legacy behavior. |
| `V2Projection.fs/.fsi` | Protected source from #3987 provides restricted exact binding and Observation-only behavior; .3 owns immutable item-target verification and actual qualification. Source fixtures are not live activation evidence. |
| `FS.GG.Coord.Cli/Client.fs` and legacy lifecycle adapters | Retain their legacy schema/lifecycle meanings; no redirection to the planning schema. Root owns shared CLI registration. |
| `Cache.fs/.fsi` | Existing owner/title board-map keys are legacy hints. V2 requires independent immutable binding/readback; no stale cached membership authority. |
| `.agents` and `.claude` driving/board/init skills | Inventory work-unified-roadmap, work-roadmap, drive-board, work-board, check-board, pnext-item and initialization during .4/.5; root owns projections/skill edits. No current adoption claimed. |
| `NewSddWorkspace` / configuration / generated guidance | Product-specific owner/project/repository scope and portable commands belong to .5/.6 publication and installed adoption; generated defaults unchanged here. |
| `coord-board-reconcile.yml` | Manual credential-free legacy diagnostic, no board writes. |
| `coord-board-archive.yml` / `coord-board-archive.py` | Separate still-scheduled legacy Project 1 archive using its App. Retain exact scope. |
| `scripts/fsgg-coord` / shared metered transport | Sole Projects operation route. Root owns composition/registration and any actual create/schema/import operation; direct GraphQL bypass is not preparation authority. |

## Field contract

Exactly four custom fields; Title/Repository/Assignees derive from the unchanged native issue.

| Field | Type and values | Owner after initial seed |
|---|---|---|
| Status | single select: Backlog, Ready, In progress, Blocked, Done | Scheduling owner |
| Roadmap | text: owning outcome/acceptance link | Owning plan owner |
| Track | single select: Active delivery, Follow-up | Scheduling owner |
| Observation | single select: Verified, Stale, Unknown | Restricted projection |

Refresh never changes scheduling fields. An initial seed conflict preserves owner edits and reports
it; no CAS or overwrite permission is inferred. Verified observation means required observations are
complete/current, not acceptance completed. Native issue lifecycle/dependencies, grants and settlement
are outside this planning surface.

## F# offline authority and fixed plan

`tools/BoardV2Import/Program.fs` is the administrative validator and stateless fixed plan constructor.
`tools/board-v2-import.py` only forwards arguments. Neither has a transport, credential path, state store
or writer. JSON Schema documents the wire; production decisions are F#. Meaningful fixtures exercise the
compiled executable, including duplicate issues, PR identities, legacy targets, incomplete visibility,
foreign repositories, every field contract, field/option identity gaps, missing acceptance, arbitrary
command input and the three-to-five bound. No lifecycle state machine was added; existing canonical Quint
and F# lifecycle authority remains unchanged. An eventual durable apply/retry/seed implementation must
map its actual semantics to canonical authority and prove production correspondence before activation.

```console
python3 tests/board-v2-import/run.py
python3 tools/board-v2-import.py docs/coordination/board-v2-import-manifest.json
python3 tools/board-v2-import.py docs/coordination/board-v2-import-manifest.json --pilot-plan
```

The final command currently refuses. Plan construction requires three-to-five approved `import` or
`follow-up` rows with `pilot=true`, `adjudication=verified-remaining`, native `I_` identity, open state,
owner/next action/outcome/acceptance, exact target and fields/options, complete organization visibility,
trusted recipe Git revision/artifact SHA-256 and repository allowlist. Dependencies are bounded to 50
per item and require a complete native snapshot before a plan can be constructed. The manifest
`authorization=root-selected` label records a selected descriptive request; it is no credential, grant
or independently sufficient effect authority. Root's actual operation controls remain required.

The constructed request contains fixed descriptive stages: fresh native source/dependencies, exact
project/schema, complete membership by immutable project and issue IDs, add only after proven absence,
initial seed retaining conflicting owner edits, and independent readback. It performs none of them.
Creation and schema establishment are separate root-selected effects and are not emitted as pilot work.
Limits: five items, 50 entries/page, ten pages/connection, two transient-read retries, 300 seconds and one
active writer for the immutable project ID. Bound exhaustion is incomplete/Unknown with cursor; lost
mutation response requires fresh readback before retry. No blind mutation loop is implemented here.

## Root-owned operation acceptance

After identity/adjudication gaps clear, qualify the real metered transport through actual composition:
complete exact membership, foreign same-number project refusal, incomplete fields/pages, denied/hidden
404, source/dependency drift, lost add/seed response, duplicate rerun, owner-edit preservation and partial
batch readback. Ambiguous create response stops for reconciliation. The native pilot must independently
read back each unchanged native issue ID, one exact membership, all selected fields and native dependency
references; repeat apply must preserve scheduling and membership. These remain explicit qualification
gaps, not claimed by the pure constructor fixtures.

No project/schema/import writes, broader transfer, scheduling switch, credential enrollment, publication
or product adoption occurred. `.2` remains open until actual target/schema/pilot readback. Telemetry
configuration is unavailable in the parent session; usage and economics remain Unknown.
