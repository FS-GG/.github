---
title: "Ordinary-v2 CI credential inventory and review"
category: Operations
categoryindex: 4
description: "Public custody inventory and fixed source-review checklist for the installed ordinary-v2 settlement candidate."
---

# Ordinary-v2 CI credential inventory and review

This inventory covers only the `ordinary-post-merge-delivery-settlement` class from
[V2-CI-I1](../roadmaps/v2-ci-i1-unattended-credential-execution.md). It does not activate a writer,
change v1 admission, or replace the `OpenV2` gate. The machine-readable authority is
[`policy/v2-ci-ordinary-settlement.json`](../../policy/v2-ci-ordinary-settlement.json).

## Public custody inventory

The `.github` Actions environment `ordinary-v2` has custom deployment branch policy `main`
(policy id `60918716`), zero required reviewers and exactly three dedicated secrets. The public
anchor binds App `5064713`, installation `164553252` and authorizer key ID
`ordinary-v2-production-6121c3f2ab38acf3`; the installed sandbox qualification uses a second App
and authorizer. The [enrollment packet](v2-ci-ordinary-enrollment.md) records the setup and the
[installed qualification](v2-ci-i1-installed-qualification.md) records its hosted evidence. Production
`credentialJob.installed` remains false until the protected `OpenV2` and exact-candidate gates.

| Environment secret name | Purpose | Current disposition |
|---|---|---|
| `V2_ORDINARY_AUTHORIZER_PRIVATE_KEY` | Sign only canonical ordinary-v2 settlement intent | Provisioned; public digest in anchor |
| `V2_ORDINARY_APP_ID` | Select the repository-scoped ordinary-v2 App | Provisioned; App ID `5064713` |
| `V2_ORDINARY_APP_PRIVATE_KEY` | Mint its short-lived installation token | Provisioned; private value stays in environment |

The v1 admission and callable isolated-operation credentials are outside this inventory and must not be
copied, renamed or accepted as substitutes. Public SPKI/App identities, the trust anchor, installation id,
permission ceiling, rotation and revocation procedure are specified in the enrollment packet and
the accepted public anchor.

## Fixed reviewer checklist

Apply this checklist to changes that touch the policy, trusted workflow, environment binding, installer
pin, credential names or public trust material. Record an exact-head evidence link; do not record private
values.

- The only admitted class is ordinary post-merge delivery settlement; release, admission, cutover,
  destructive and arbitrary command classes refuse before credential access.
- The trigger is an automatic push to protected `main`. The commit association endpoint returns exactly
  one merged PR whose `merge_commit_sha` equals the pushed source. PR and manual dispatch events refuse.
- The secret-free predecessor binds source, workflow revision, policy digest, environment, operation class
  and the complete required-check population. Missing, failed or stale evidence refuses.
- Only the separate credential job names `ordinary-v2`; it runs on a GitHub-hosted ephemeral runner after
  the predecessor. PR jobs, forks, local/self-hosted Main or Work runners, fdev and host agents have no
  credential route.
- Credential names and public identities match the dedicated v2 inventory. No v1 or callable key is
  reused, and logs, artifacts, command arguments and receipts contain no private material or tokens.
- Permissions stay within the selected repository and required write/readback operations. The installer
  is immutable and does not expose general signing, token issuance or arbitrary command entry points.
- One stable operation/attempt identity survives workflow reruns. Expected-absent/CAS, unknown-result
  reconciliation, independent readback and replay refusals remain exercised.
- v1 genesis and `OpenV2` remain unchanged. The completed provisioning, anchor enrollment and installed
  rehearsal do not bypass the separate receiver activation decision.

The reviewer can request source repair. The reviewer does not hold credentials, approve as another person,
or authorize a protected effect.

## Concurrent-main source composition repair — 2026-10-07

The original protected-source predecessor `37589450783/a1` at `862a830c` refused before
credential execution: PR4305's current target base moved to `a37a1b7f`, while its head
`2c738455` and the squash parent have graph merge base `65c8d8ce`. That failed attempt
remains failed. The selected repair distinguishes checked head H, triggering source S,
S's sole parent P, unique graph merge base B, and stable current PR target base C.
C is observed separately and is never called a check-time qualification base.

The trusted native observer binds exact commit/tree identities and reproduces S's tree
with a clean three-way merge of P/H at independently verified unique B. An isolated
Git repository uses one depth-256 fetch and a shared 120-second graph budget; ancestry
must be complete above B. Reachable shallow boundaries outside B's ancestry refuse;
shallow history at/below already proven common B cannot hide another maximal base.
Ambiguous bases, contradictory native/local objects, conflicts, extra source edits,
output/deadline failures and changing PR or protected authority retain refusal.
The unchanged receipt records C as `pullRequestBaseSha`, H as `qualificationSha`, and
the proven **source S tree** as `qualifiedTreeSha`, matching the pinned receiver's
source-tree interpretation. Complete exact-H native check/producer/attempt validation
and independent same-run receipt recomputation remain mandatory.

Preflight reuses static workflow boundaries and focused native Git controls; no custom
model is selected because job topology/order and effect boundaries are unchanged.
Local controls are serial, CPU0, within a 180-second/256-MiB light-helper envelope;
real local graph fixtures cover clean concurrent squash, criss-cross bases, shallow
frontiers and refusal/cleanup. Byte composition is separate from coherent validation
under ADR-0084. Source qualification and later native predecessor/receiver outcomes
remain separate; this repair authorizes no rerun, credential action, intake operation13
reset, grant renewal, journal mutation or workspace/default adoption.

Local source controls passed: 31 observer tests, including real Git graph/merge and
receipt drift cases; 10 qualification tests; routine eligibility and operation-boundary
fixtures. Both demonstrated regressions fail on protected `862a830c` and pass the
repair. The expanded observer run used CPU0, peaked at 60,194,816 bytes of sampled
owned process-tree RSS/two processes, and completed in 1.14 seconds; all directly
owned children were terminal. Fixture transport copies local objects and constructs
real shallow metadata, so no network/native credential path was executed. Hosted
coherent qualification, source delivery and original-identity native readback remain
pending; these controls establish no production settlement or receiver adoption.
