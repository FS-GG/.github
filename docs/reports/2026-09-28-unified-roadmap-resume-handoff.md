# Unified roadmap resume: V2 pilot cutover and preserved source evidence

Latest landing readback: `.github#3914` merged as `ebb70d66db055c9a92e2627776a89dcbf4b3fbfe`;
Coordination docs PR #864 merged as `39e18d16c1055ae6dc3cdb875fb3812929d58fff`. Both used
head-conditioned native merges after required checks under ADR-0091. The older optional Coordination
coherent run remains separate background evidence, not clean-start migration acceptance. Both active
roadmaps now describe the simplified strategy; `.github` records completed pilot/replay acceptance.

This session resumed the [September 27 handoff](2026-09-27-1919-unified-roadmap-handoff.md) and inspected current source, PRs and protected artifacts. The programme remains incomplete. The initial recovery phase performed no source merge, publication, installed activation or native acceptance; the later clean-start authority cutover is recorded below.

## Latest steering: use the V2 pilot frontier

The September 28 user steering prioritizes speed before correctness and explicitly removes backwards-validity and V1-critical requirements from the active frontier. The native-admission and migration continuation recorded below is superseded as an execution plan. Keep it as historical source and qualification evidence; do not resume it as the current route.

The V2 pilot source from [`.github` PR #3913](https://github.com/FS-GG/.github/pull/3913), exact head `09e1347e4f9b266ff2056d62aae34cc5365460f9`, merged as `a98162fb119c43f2e5d60c2b284d01e81ac468db`. Workflow run [36395767759](https://github.com/FS-GG/.github/actions/runs/36395767759) then produced the following bounded sequence:

- attempt 1 stopped on an observer failure before any effect;
- attempt 2 retried failed jobs only and found no artifact;
- full attempt 3 returned `SettlementSucceeded`;
- full attempt 4 returned `SettlementAlreadyComplete` with the same settlement digest, `96eebd38b0639d4c446d62dcfa413adffae32c839b387006d68322b1ca7399d6`.

Independent native-journal readback found `refs/heads/fsgg/v2/journal/operation/a1` at commit `16aad7f7d7bb64885a929a98230b31724fab3603`, generation 3, stage `complete`. Replay left that ref unchanged. Clean pilot checkpoints C0–C2 are complete. Continuous operation is enabled for `.github` only; enabling any other repository is an explicit later action.

The current follow-up is [`.github` PR #3914](https://github.com/FS-GG/.github/pull/3914), exact head `85ca91fe00859ed5c62ec26b692e3d7182cfbf61`. It contains the artifact helper and bounded read-retry repairs plus both roadmap completion projections. Its CI is pending, so this report does not claim it merged. [Coordination PR #864](https://github.com/FS-GG/FS.GG.Coordination/pull/864), exact head `de2880aa01b0dba8ea34cc9ee87bcbffb39bb909`, also remains open; its coherent required checks have passed, but this report does not claim it merged.

Superseded `.github` PRs [#3911](https://github.com/FS-GG/.github/pull/3911) and [#3912](https://github.com/FS-GG/.github/pull/3912), and Coordination PRs [#862](https://github.com/FS-GG/FS.GG.Coordination/pull/862) and [#863](https://github.com/FS-GG/FS.GG.Coordination/pull/863), are closed with their branches preserved. V1 workflow run `365481455` was observed in `disabled_manually` state. No V1 work remains active.

The native clean-start authority cutover completed at ref `26d1882af9293b264df17a1fa98515e108313fe5`, parent `42a25b1480203207183f37c56d315c4161fb627b`, with generation 2 open for V2. Exact original rule `22627740` was restored with sole App `4882399` and `current_user_can_bypass=never`; ordinary and integrity settings were unchanged. The retained receipt is `/tmp/gs2-clean-authority-prestate/execution-20260928T075750Z-508098/receipt.json`, SHA-256 `e4c6b9736dce16eb7b3434c3b9c394d77aab61b010db250a19f02234ef88df58`.

The production workflow reuses CLI `0.1.2`. The completed rollout changes only continuous operation in `.github`; it does not expand the registry or runtime. Focused qualification passed in three groups: 41, 9 and 13 tests. The parent source worktree is `/tmp/roadmap-gs2-pilot-integration`. Telemetry remained `not-configured` throughout.

## Superseded V1 delivery boundary

The green receiver-observer candidate was submitted to `tools/routine-delivery.py --apply`. The helper refused before a merge attempt because its production `DisabledV1AdmissionPort` has no installed replacement. The [governing design](../coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md#49-cutover-epoch) requires the common service, issuer, journal and provider probes before direct routine merges. Its separate one-time installation decision must bind exact source heads, ordered bases, installer/artifact bytes, native owner approval, credential scope, expiry and independent readback. ADR-0087 genesis does not authorize this installation.

Fresh read-only protection inspection found `.github/main` enforcing administrator checks, with `restrictions=null` and no effective branch rules returned. It did not establish the required provider-enforced exclusive base-writer boundary. The current token does have repository administration permission, but fresh organization membership, organization ruleset and organization App-installation reads each returned HTTP 403 (`Resource not accessible by personal access token`). Repository rulesets with inherited rules requested returned only repository rules `23535685` and `19899954`; this is not complete organization/App authority. Direct `gh pr merge` was not used as a fallback. Broad decision authorization cannot supply missing native approval, installation or credential evidence.

The existing unapproved bootstrap proposal is retained in Coordination branch `routine/v1-admission-service-bootstrap-proposal`, commit `258727a`, under `work/gs2-v1-admission-production/`. It contains unset native-evidence fields and is not an accepted installer. The genesis journal remains an accepted input only.

## Admitted source PRs

| PR | Exact candidate | Delivered scope and limit |
|---|---|---|
| [Historical census #3911](https://github.com/FS-GG/.github/pull/3911) | `f1d8c7cc915c3f0f3f75e24c542a2988fc0d68c0` | Repaired CLI-test workflow selection, transport census digest and FSharp.Core lock hash. No private two-pass capture or historical-loss approval. |
| [Receiver observer #3912](https://github.com/FS-GG/.github/pull/3912) | `f247616964d07c84008dbfaece3e81a198e24f48` | Recovered stable repository comparison, excluding only validated temporary clone tokens while retaining raw digests and meaningful drift refusal. Protected rerun pending merge. |
| [Callable composition #862](https://github.com/FS-GG/FS.GG.Coordination/pull/862) | `dfd1363343733933cc4ba5b2fca0d47b827702e1` | Host-selected candidate observation joins protected installed-runtime validation. No production InstalledHost/ProtectedAuthority or native /5 operation. |
| [Settings composition #863](https://github.com/FS-GG/FS.GG.Coordination/pull/863) | `c7980c1dfa6480223134fa690940076a5b1e6066` | Recovered canonical eleven-surface settings bridge and fresh two-pass raw/typed revalidation. Provider permissions remain unresolved. |

The admission campaign remains `org-pr-backlog-20260927`. Both repositories have two managed delivery PRs; prepared additional source stays outside new PR admission. Before resuming, read exact live heads and checks. Advisory board reconciliation still fails because production V1 writes and some unrelated delivery-route receipts are unavailable; changing these candidates cannot supply that authority.

## Protected observations

Receiver run [36380910093](https://github.com/FS-GG/.github/actions/runs/36380910093) refused at `pass-1-repository-repeat`; token revocation succeeded. It granted no copy or mutation authority. #3912 repairs the identified comparison failure but has not been run from protected main.

Settings diagnostic [36369740374](https://github.com/FS-GG/.github/actions/runs/36369740374) retained provider 403 responses for multiple organization/repository settings. Diagnostic success is not complete settings authority.

The callable candidate workflow has no selected successful run/artifact. Its bounded candidate observation requires freshness, so producing an artifact before the protected host is ready is not useful acceptance. Existing /4 evidence binds old source/CLI and a target now returning 404. It cannot establish /5. The production host and ten scoped role identities are absent; this environment exposes one credential identity, not the required distinct protected roles.

## Preserved historical work and gates

The section 0 correction for SKILL-FS-01.1–.4 source closure is prepared on `routine/unified-resume-progress-20260928`; it is not a protected-main fact yet. SKILL-FS-01.5 remains held behind that mandatory projection. Astra's bounded discovery/publication/caller-switch plan is retained locally in `/tmp/roadmap-skill-conversion/SKILL-FS-next-window.md`; planned release identities require rechecking before any use.

Q4 seed/bootstrap, ordered migration execution, common admission source and LEARN-01.2 work are retained in isolated local branches. The following checkpoint identifies qualified commits; active working-tree changes remain unfinished until their owner commits and reports qualification. They do not clear protected acceptance gates.

Templates inspection found its main and recent receiver qualification green, but the unmerged Babylon successor remains a dependency for the next FBX work. The earlier second-runtime object `ebfbc348` was absent from local objects, remote refs and GitHub; it was not falsely reported recovered. No Templates change was made.

The Main-host dependency belongs to the legacy V1 bootstrap. [ADR-0088](../adr/0088-ci-owned-unattended-credential-execution.md) places future ordinary V2 credentials on remote GitHub-hosted runners without a host relay; the currently qualified class is post-merge settlement and excludes admission/bootstrap. It is not an alternate merge route.

Before the V2 steering, the next native sequence under the existing contract was explicit source/installer qualification, isolated proof of writer exclusivity, the exact protected administration and installation decisions, installed common admission/issuer/provider probes, and only then ordinary source delivery. The migration plan then required full provider capture, effect/recovery/archive/rollback, two-round rehearsal and independent omission acceptance. Observation windows and actual installed receiver evidence remained separate.

Telemetry `begin` returned `not-configured` for root, child and follow-up attempts; no token was returned for `started`/`finish`. Usage, cost and bureaucracy percentages remain unknown. No observer or periodic service was installed.

## Local qualification checkpoint

- LEARN-01.2: `routine/learn-01-2-observation-20260928` at `4db459ce5730ed5f2915af7b50877e616c5f3b6b`, worktree `/tmp/roadmap-learning-observation`. Schema 11 retains durable fact order; schema 12 adds receiver-authenticated receipt and first-fact provenance. Protected host-config/2 grants cannot be injected through generic envelopes or upgraded by replay. Private snapshot v4 excludes credentials and is accepted by the dashboard. CLI 558, telemetry 28, dashboard 20, analysis 37, native-source 12 and claim-generation 59 tests passed; independent review found no consequential defect. Dashboard qualification used an isolated NuGet cache after the existing global FSharp.Core hash disagreed. Shared-cost comparison remains unqualified pending native-source custody and trusted snapshot acquisition; .2 stays open. PR body: `/tmp/learn-01-2-pr-body.md`. No publication, enrollment or native collector installation occurred.
- Q4 seed and migration execution: `routine/gs2-09-7-q4-seed-recovery-20260928`, worktree `/tmp/roadmap-q4-seed-recovery`. Execution `141e6b4` was integrated as `54f01f3`, followed by CI registration `5151536` and project census `5b08922`. Combined build passed with zero warnings/errors, seed tests 42 and execution tests 17 passed. Architecture 691, full unit 1127 and Host 93 tests passed. The comprehensive gate passed locked restore, warning-as-error build and every test stage, then its final installed telemetry receiver fixture refused this container’s overlay filesystem as an unsafe store root. No storage guard was weakened; that fixture still requires a supported durable filesystem. The controlled execution harness is process-crash qualified only; protected production refuses unqualified power-loss durability. No production CLI execution route or native effect is enabled.
- Common admission: `routine/v1-admission-source-integration-20260928`, worktree `/tmp/roadmap-admission-integration`. Recovered source `deb2bf1` and project registration `5ebbeda` are followed by qualified inactive process commit `daaa50f63af038fc0864081662129a68d176c686`. Review found and repaired duplicate-send concurrency, nonterminal settlement blocking later Applied, and fresh readback disagreeing with durable settlement. Deterministically concurrent signed requests now have one durable effect-level send owner. Unit 1097, Host 93, Architecture 691 and Python admission controls 50 passed; the final compiler-gate telemetry fixture remains unavailable on the overlay filesystem. Installer integration `aa40907` cherry-picks `9c07178`; combined Q6 passed. This process qualification is local Git/loopback, not a native GitHub transport. Its further source window is preserved below as superseded history.
- Installer preparation: `/tmp/roadmap-admission-installer`, branch `routine/gs2-v1-admission-installer-20260928`, committed `9c07178d022060a59d490f5a3129a093f5f97b8a`. It contains separately bound administration, source-installation and runtime-activation candidate manifests, bounded preparation/verification and an explicitly loopback-only one-attempt qualification executor. Focused tests 10/10 and the full Q6 operational validator passed. No production executor or native approval verifier is supplied by this isolated harness. No native approval, rule change, merge or runtime activation is inferred.

#3911 and #3912 completed their source checks; the advisory board reconciliation remains red as described above. #862 completed its checks. #863 attempt 1 had three diagnosed tool failures: claim-election and rollback exceeded their Apalache execution limits after matching simulation checks; formal-base's legacy verifier failed during process startup after its preceding checks passed. Two aggregate failures followed those missing fragments. Identical formal inputs passed on #862. After independently verifying the exact source head, completed first attempt and complete failed-job set, one `--failed` retry was requested for run `36386099898`. No source, bound or required check was weakened. The retry passed. Fresh PR readback found #862 and #863 open with no unfinished or failed checks; #3911/#3912 retain only the documented advisory reconciliation failure.

LEARN review repair `450b173c` kept shared-cost comparisons unqualified, with generic-producer regressions; parent Unified correction `c667955a` kept .2 open. The subsequent bounded source window covered protected host credential role/grant, immutable authenticated receipt provenance and first-fact admission provenance. Collector role alone did not verify native source or imported snapshot origin. These results remain historical evidence under the V2 steering above.

## Preserved superseded native-transport source window

Astra's bounded plan identified the following implementable source gaps before the V2 pilot superseded this execution route:

- `callable_activation` owned the Coordination native authority/journal/CAS adapter, credential-isolated fixed GitHub merge adapter, and trusted routine request translation on `/tmp/roadmap-admission-integration`. The scoped Authority transport was not to be broadened to source PR merges. Its native path had to preserve response-unknown semantics and the independent durable send owner. The discovered `RegisteredMergePolicy` bug treated its second argument incorrectly; that argument is verified `merge_actor_id`, not the PR number. This source qualification authorized no live provider effect.
- `settings_composition` owned the disabled-by-default `.github` consumer in isolated `/tmp/roadmap-admission-consumer`, starting from `cf30b757`. It was to pass caller lookup hints to the pinned trusted executable rather than manufacture typed authority. Caller, process, native request, Applied proof and normalized response digests had distinct meanings and required explicit verified translation.
- This superseded route required native credential/custody policy, complete organization/App/exclusive-writer evidence, protected artifact installation, live probes and independent readbacks before production activation. Neither the existing genesis decision nor ADR-0088's ordinary-v2 post-merge class supplied those joins. The `.github` one-time source-installation scope did not authorize Coordination source installation.

Both dispatches retained GS2-09 lineage and returned telemetry `not-configured`, with no token. At that checkpoint, no native merge, publication or activation had occurred.
