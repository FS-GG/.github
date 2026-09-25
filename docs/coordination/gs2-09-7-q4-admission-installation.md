# GS2-09.7 Q4 route admission and installation hold

Status: **read-only decision packet; no admission or installation**. This is
stacked on draft #3749. The inspected remote default branch was
`origin/main` at `2e553e41e58ee2f5e27aedcffc7403ce50e7cdd4`.
Protected workflow PR #3690 was merged as source at
`ff425734d277fa54c3d71601da90fe7b22619c15`; that merge is not an
OperatingV1 protected admission decision or a GS2-09.7 Q5/Q6 receipt.

## Installed route and exact gap

The default-branch
[Q4 workflow](../../.github/workflows/github-substrate-v2-sandbox-qualification.yml)
is manual, checks out an exact Coordination candidate, uses the fixed
`github-substrate-v2-registered-sandbox` concurrency group and protected
`github-substrate-v2-sandbox` environment, and references the existing
`FSGG_DISPATCH_APP_ID` and `FSGG_DISPATCH_APP_PRIVATE_KEY` secrets. It pins
actor `fs-gg-cross-repo-dispatch[bot]` (ID `297630107`), private repository
`FS-GG/FS.GG.GitHub.Substrate.Sandbox` (node `R_kgDOUKXpqQ`), Project 2
(node `PVT_kwDOEYAWY84BiESo`), and purpose `fsgg-sandbox-gs2-04-9`. Its
nonce is run ID, attempt and candidate SHA. #3690 added selected-repository
mint-grant proof before the candidate route. The separate mint-proof workflow
checks token grants and revocation; it does not run migration.

The Q4 workflow still invokes `eng/qualify-github-sandbox-closure.sh` for
GS2-04.9. It passes the minted token directly to candidate execute and
cleanup, then invokes native token revocation. It does not invoke the stacked
GS2-09.7 host signer, independent admission port, one-use release, durable
vault/claim/journal, finalizer, scheduler or recovery worker. Those files are
absent from the default-branch route. Source references to secrets do not
prove their current value, installation grant or ACL; a selected-repository
grant does not itself restrict the App's organization Project write scope to
Project 2. No new sandbox copy or maintainer-provisioned credential is
required by [ADR-0089](../adr/0089-reuse-the-protected-q4-sandbox-for-migration-rehearsal.md).

The missing **OperatingV1 admission** is an independent protected decision
for the reviewed workflow revision and exact candidate, before this route
may be treated as GS2-09.7 execution. The source-only
[revision hold](gs2-09-7-protected-revision-admission.md) requires an
immutable admission record outside the workflow checkout and candidate
workspace, with workflow path/SHA, candidate SHA, run ID/attempt, nonce,
sandbox target, signer identity and release policy revision. The host must
authenticate and read it through a separately pinned protected identity
before App secret use. `github.sha`, a runner environment value, candidate
consistency proof or merged PR cannot supply that independent decision.
The stacked source pin for admitted workflow SHA is empty, as are signer,
admission, store, vault, revoker and recovery production pins. There is no
installed claim that #3690 has passed this admission.

## Protected-owner actions, in order

1. The OperatingV1 admission/release owner independently reviews the exact
   protected workflow and Coordination candidate, publishes an immutable
   admitted decision with the binding above, and supplies native current,
   unrevoked readback and resource/policy digests. The current #3690 merge
   needs its own admission; any later migration workflow revision needs its
   own exact review and decision. A self-asserted workflow SHA refuses.
2. The protected `.github` workflow and credential custodian verifies the
   **existing** App/installation actor, selected repository and effective
   grants, Project target, environment and concurrency policy, then proves
   host-only secret, signer, token-vault and durable-store ACLs. Review exact
   installed endpoint/resource IDs, signer SPKI and policy pins, candidate
   write denial, and native readback. Do not place a private key, raw token or
   writable claim record in the candidate workspace or artifact.
3. The release/store/revoker owners jointly install and qualify the source
   contracts only after step 1: mint-to-pending census completeness, signed
   joint head and monotonic floor, decision-ID one-use claim, admission/revoke
   launch interlock, one shared native-attempt namespace, durable scheduler
   and finalizer, and token-safe revocation. Resolve the documented
   [emergency native-revocation choice](gs2-09-7-emergency-native-qualification-packet.md):
   provider idempotency, authoritative settled token readback, or protected
   containment with the receipt held. A fake port cannot choose this policy.
4. The Coordination migration and protected sandbox owners complete the
   accepted nine-authority interpreter and Q5/Q6 gate, then separately run
   the isolated representative rehearsal, interruption/recovery matrix,
   independent native receiver/effect readback, rollback and second round.
   Only the protected acceptance owner may bind that evidence into a Q5/Q6
   receipt. See the [accepted rehearsal contract](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/gs2-09-7-representative-rehearsal.md).

## Non-effect refusal controls for the installation review

| Injected observation or read-only mismatch | Required disposition |
| --- | --- |
| Missing, foreign, stale, revoked or self-asserted admission; changed workflow SHA/path, candidate, run ID, attempt or nonce | Refuse before signing, mint or token handoff; no source merge is substituted for native admission. |
| Wrong App actor/installation, repository node, Project node, selected repository, grant, purpose or changed target | Refuse before candidate/provider call; retain the native observation without a raw token. |
| Empty or drifted signer/admission/store/vault/recovery pin, candidate-writable ACL, duplicate decision-ID mint, lost CAS result | Refuse handoff or retain durable pending state; never infer one-use from token expiry or process memory. |
| Minted token absent from a sealed pending census, duplicate job, withdrawn job, scheduler crash or unknown provider result | Keep pending, fence another launch, use exact native readback and shared attempt identity; never close by a partial scan. |
| Native revoke timeout, store outage, crash after attempt marker or unknown token state | No successful revocation or Q5/Q6 receipt until settled native evidence and durable receipt; use the owner-selected emergency policy. |
| Q4 closure or mint-proof green, source-only fake test green, partial migration read, or missing terminal page | No GS2-09.7 Q5/Q6 acceptance. |

The existing fake-port negatives characterize these refusals but do not prove
the installed protected principals, ACLs, provider semantics or native
terminal state. All installation evidence must identify exact run and
attempt, candidate/workflow heads, resource IDs, policy/key digests, native
request/attempt IDs and readback timestamps while excluding secret material.
Until the owners supply the protected decision and installation evidence,
Q5/Q6 remain open and the existing Q4 route is the only installed sandbox
effect route observed here.
