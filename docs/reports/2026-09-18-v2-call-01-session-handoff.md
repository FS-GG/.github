# V2-CALL-01 session handoff

Status: source preparation complete; protected execution not authorized  
Recorded: 2026-09-18  
Resume item: `V2-CALL-01.4b`

## Stable handoff state

The callable-v2 package and receiver remain pinned at `FS.GG.Coordination.Cli` 0.1.0. This session
completed the guarded operator, protected grant producer, source-only executor, artifact-envelope
contract, and telemetry recovery needed to reach the external-authority boundary. No workflow was
dispatched, no credential was minted, and no GitHub environment, App grant, target repository,
provider resource, epoch, journal, package, or acceptance state was changed.

The authoritative default-branch readbacks at handoff are:

- `FS-GG/.github`: `d12ea72fda81456c5e5f76e7af38219da2d140f4`;
- `FS-GG/FS.GG.Coordination`: `d46aa238d0f169c85a5822e62e49ab9df1ebf37d`;
- telemetry host: `ready`; the root attempt ended `cancelled` with exit code 130 because the user
  requested this handoff, and its queue drained completely.

The local `.github` checkout was clean before this report was created. All seven completed callable
implementation worktrees and their local topic branches were removed. The only remote topic branch
left by the session was also deleted after its PR merged. Unrelated worktrees were not touched.

## Landed evidence

| Result | Evidence |
| --- | --- |
| Guarded isolated-operation source preparation | Coordination [PR #435](https://github.com/FS-GG/FS.GG.Coordination/pull/435) |
| Protected grant producer | `.github` [PR #3546](https://github.com/FS-GG/.github/pull/3546) |
| Operator authority, recovery, and replay hardening | Coordination [PR #436](https://github.com/FS-GG/FS.GG.Coordination/pull/436), 44 hosted successes and 5 expected skips |
| Source-only protected executor | `.github` [PR #3547](https://github.com/FS-GG/.github/pull/3547), 56 hosted successes and 3 expected skips |
| Non-circular artifact-envelope contract v4 | Coordination [PR #437](https://github.com/FS-GG/FS.GG.Coordination/pull/437), 44 hosted successes and 5 expected skips |
| Grant/executor v4 repin | `.github` [PR #3548](https://github.com/FS-GG/.github/pull/3548), 58 hosted successes and 3 expected skips |
| Rejected telemetry-publication recovery | `.github` [PR #3549](https://github.com/FS-GG/.github/pull/3549); the actual stale complication was released, corrected, recorded, and drained |

Contract v4 uses logical contract digest
`3ebf436e7e2efdf221b9b08f96b6d5216bbeb22053af26bd7cdd2d0d11ef561d` and operator digest
`b5a20b2c511bf37833dac99c35eb1fa420f410f5b324fd26883e5af928cb145c`. The canonical grant payload
contains no server-assigned artifact coordinates. A separate
`fsgg.coordination.callable-isolated-operation-grant-artifact-envelope/1` binds the repository,
artifact ID and run/attempt-qualified name, archive and payload digests, workflow run and attempt, and
expiry. The operator independently rereads and validates that envelope and the single canonical file
inside the downloaded ZIP.

The current executor workflow is intentionally readiness-only. It has constant concurrency,
evidence-only inputs, immutable Coordination/source pins, and no reachable credential-mint or provider-
effect path. Its `prepared-not-authorized` result is not `.4b` acceptance.

## External authority still required

The final read-only audit returned 404 for both:

- the `FS-GG/.github` environment named `callable-isolated-operation`; and
- `FS-GG/FS.GG.Coordination.CallableSandbox`.

The existing `fs-gg-cross-repo-dispatch` App inventory was previously read back successfully by
[permission-coherence run 35352143132](https://github.com/FS-GG/.github/actions/runs/35352143132).
It still does not establish the required `checks:read`, `workflows:write`, or organization
`members:read` capabilities. Environment creation and protection, App permission changes or App
selection, installation scope, reviewer membership visibility, and credential custody are external
administrative actions. Broad roadmap authorization does not substitute for those protected actions.

The typed cross-repository intake was also attempted and refused because production-v1 admission and
its durable operation journal are unavailable. That refusal is recorded as telemetry complication
`intake-production-v1-admission-refusal`. Do not bypass it with a direct issue write.

## Resume sequence

1. Re-read the environment, target repository, App installation/grants, reviewer membership, and
   permission-coherence inventory. Treat absence or unreadability as a refusal, not approval.
2. Obtain the external administrative changes through the protected owner route: create and protect
   `callable-isolated-operation`, establish the required reviewer/source policy, and make the exact
   credential roles and permissions observable.
3. Update and requalify the readiness-only executor before dispatch so it can mint the separately
   scoped creation, setup, execution, and cleanup credentials. Do not hide unavailable permissions in
   a helper or mark them present in the inventory before live readback.
4. Prepare immutable plan evidence and obtain a completed protected grant for the creation phase.
   Execute only that phase, retaining its intent and authoritative creation receipt.
5. Derive the identity-bound plan from the verified receipt, obtain a separate grant, mint an exact
   singleton-target execution credential, and run the installed 0.1.0 CLI with native readback and
   restart-safe cleanup.
6. Record `.4b` only after the installed isolated-provider operation settles. Perform `.4c` external
   acceptance separately; then continue `.5`, Q4, OpenV2, and the remaining GS2-09 rehearsal work.

Do not dispatch either protected workflow while the environment or credential roles are absent. Do
not interpret the source-only executor, a 404 target, a pending/unknown provider result, or telemetry
health as installed-provider acceptance.

## Read-only restart checks

From `/home/developer/projects/.github`, a new session can confirm the handoff without mutation:

```bash
git status --short
git rev-parse HEAD
gh api repos/FS-GG/.github/environments/callable-isolated-operation --include
gh api repos/FS-GG/FS.GG.Coordination.CallableSandbox --include
FSGG_TELEMETRY_REPOSITORY=FS-GG/.github \
  fdev-telemetry exec python3 tools/roadmap-telemetry.py status
```

Expected at this handoff: a clean tree, `.github` main at the SHA above, two 404 responses, and
telemetry status `ready`. A changed result is new evidence and must be reconciled before resuming.
