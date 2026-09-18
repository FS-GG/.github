# V2-CALL-01 protected execution handoff

Status: source and protected authorization ready; provider execution not started

Recorded: 2026-09-19

Resume item: `V2-CALL-01.4b`

## Resume from this state

The callable-v2 package and receiver remain pinned at `FS.GG.Coordination.Cli` 0.1.0. The source,
environment, dedicated GitHub App, immutable creation plan, and protected authorization path are
qualified. No executor run has occurred, the disposable target repository remains absent, and no
provider, journal, epoch, cleanup, or acceptance effect is claimed.

Authoritative default-branch revisions:

- `FS-GG/.github`: `552ac96c56f8d2dbb7ae6480847039c2ed26b87a`;
- `FS-GG/FS.GG.Coordination`: `d46aa238d0f169c85a5822e62e49ab9df1ebf37d`;
- telemetry host: `ready`.

The local `.github` checkout has an unrelated untracked `_apalache-out/` directory. Preserve it.

## Landed source and authority

| Result | Evidence |
| --- | --- |
| Guarded isolated-operation source | Coordination [PR #435](https://github.com/FS-GG/FS.GG.Coordination/pull/435) |
| Operator authority and recovery hardening | Coordination [PR #436](https://github.com/FS-GG/FS.GG.Coordination/pull/436) |
| Artifact-envelope contract v4 | Coordination [PR #437](https://github.com/FS-GG/FS.GG.Coordination/pull/437) |
| Protected plan, grant, and live executor source | `.github` [PR #3552](https://github.com/FS-GG/.github/pull/3552), merge `cea1f3d50e72649cfe0b31f2a6d4641f05825c46` |
| Client-ID token migration | `.github` [PR #3553](https://github.com/FS-GG/.github/pull/3553), merge `552ac96c56f8d2dbb7ae6480847039c2ed26b87a` |
| Current immutable creation plan | [run 35406307859](https://github.com/FS-GG/.github/actions/runs/35406307859), artifact `10572254613` |
| Current protected authorization | [run 35406365841](https://github.com/FS-GG/.github/actions/runs/35406365841), artifact `10571744877` |

Contract v4 uses logical contract digest
`3ebf436e7e2efdf221b9b08f96b6d5216bbeb22053af26bd7cdd2d0d11ef561d` and operator digest
`b5a20b2c511bf37833dac99c35eb1fa420f410f5b324fd26883e5af928cb145c`.

The environment `callable-isolated-operation` has id `22246772831`, sole required reviewer
`EHotwagner`, self-review permitted for that accountable operator, and one custom `main` deployment
policy. App `4995487`, installation `162873149`, is installed for all FS-GG repositories with the exact
reviewed permissions. The environment contains these secrets:

- `CALLABLE_ISOLATED_OPERATION_APP_CLIENT_ID` for installation-token minting;
- `CALLABLE_ISOLATED_OPERATION_APP_ID` for App JWT and installation identity validation;
- `CALLABLE_ISOLATED_OPERATION_APP_PRIVATE_KEY` for protected token minting.

Do not print, copy, rotate, or replace their values during ordinary resume work.

## Current plan and grant identities

The current creation plan is source-bound to `.github` merge `552ac96c…`:

| Field | Value |
| --- | --- |
| Plan run / attempt | `35406307859` / `1` |
| Plan artifact | `10572254613` |
| Plan artifact name | `callable-isolated-operation-plan-creation-35406307859-1` |
| Plan archive SHA-256 | `311094e89ecefb94a8775a01ebc810fb530cfd7b7dc588033796f60af0755325` |
| Plan payload SHA-256 | `0e7b32ed9af86b1cd4a969cb0bcddc2736c33b528dd0ef49ecb3adf8bbb15f9f` |
| Plan seal | `a4d504874b09a63b2cc3ce3de61da4f7e4714716d79ce7d52b5bb62cdd9909e0` |
| Artifact expiry | `2026-10-18T23:36:00Z` |

The latest authorization proved Client-ID token minting without the former `app-id` deprecation
warning:

| Field | Value |
| --- | --- |
| Authorization run / attempt | `35406365841` / `1` |
| Grant artifact | `10571744877` |
| Grant artifact name | `callable-isolated-operation-grant-35406365841-1` |
| Grant archive SHA-256 | `8118ef550bcae05c9f9336d7433666f6e268defcb4174144e41cb681950720be` |
| Grant payload SHA-256 | `066f129fa5025f5ad396b81e63f44b159494d66ced12cfd86a717858aba9b8ef` |
| Grant payload expiry | `2026-09-19T00:07:00Z` |
| Artifact expiry | `2026-09-19T23:37:01Z` |

The grant payload is deliberately short-lived. Treat it as expired unless current UTC is strictly
before its payload expiry. Artifact availability does not extend grant authority.

## Safe resume sequence

1. Read `.github` `main`, Coordination `main`, the environment, both artifacts, target repository,
   protected workflow runs, and telemetry status. A changed source head invalidates the plan and grant.
2. If the recorded grant payload has expired, dispatch
   `.github/workflows/callable-isolated-operation-authorize.yml` again for phase `creation`, using the
   current plan identities above, environment `22246772831`, both contract digests, Coordination revision
   `d46aa238…`, current `.github` revision, and a 30-minute grant lifetime. Approve the environment as
   `EHotwagner`, then bind the new run, artifact, archive digest, payload digest, and expiries.
3. Dispatch `.github/workflows/callable-isolated-operation-execute.yml` for phase `creation` only with
   the exact plan and unexpired grant envelope. Leave every creation-receipt and checkpoint input empty
   on the first attempt. Observe the run and retain its authoritative creation receipt.
4. If creation returns unknown, pending, or interrupted, do not issue a fresh logical operation. Use the
   retained checkpoint and receipt identities to resume the same operation, following executor readback.
5. Generate the `identity-bound-operation` plan from the verified creation receipt. Obtain a separate
   protected authorization grant for that phase.
6. Dispatch the identity-bound executor. Require installed 0.1.0 CLI execution, PR and sharded-journal
   readback, fresh-process replay without duplicate effect, durable cleanup intent, and confirmed target
   deletion.
7. Record `.4b` only after provider settlement and cleanup are authoritative. Do not start `.4c` in the
   same acceptance claim; `.4c` independently qualifies installed native execution.

Never reuse an expired grant, infer completion from a successful workflow shell, manually create or delete
the target, substitute the local PAT for an App role, or treat an absent/unknown provider result as settled.

## Expected restart readback

At this handoff:

- `.github` `main` is `552ac96c56f8d2dbb7ae6480847039c2ed26b87a`;
- all three environment secret names are present;
- the plan and authorization runs above completed successfully;
- executor workflow run count is zero;
- `FS-GG/FS.GG.Coordination.CallableSandbox` returns HTTP 404;
- telemetry status is `ready`.

Any difference is new evidence. Reconcile it before dispatch rather than restoring this snapshot blindly.
