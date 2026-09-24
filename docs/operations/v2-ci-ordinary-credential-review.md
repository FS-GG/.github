---
title: "Ordinary-v2 CI credential inventory and review"
category: Operations
categoryindex: 4
description: "Public custody inventory and fixed source-review checklist for the prospective unattended ordinary-v2 settlement path."
---

# Ordinary-v2 CI credential inventory and review

This inventory covers only the prospective `ordinary-post-merge-delivery-settlement` class from
[V2-CI-I1](../roadmaps/v2-ci-i1-unattended-credential-execution.md). It does not activate a writer,
change v1 admission, or replace the `OpenV2` gate. The machine-readable authority is
[`policy/v2-ci-ordinary-settlement.json`](../../policy/v2-ci-ordinary-settlement.json).

## Public custody inventory

The `.github` Actions environment `ordinary-v2` was observed on 2026-09-24 as an inert shell: custom
deployment branch policy `main` (policy id `60918716`), zero required reviewers and zero secrets. It is
not installed operation evidence. Milestone 04 owns enrollment of new public identities and secret
provisioning through the GitHub App browser manifest flow.

| Future environment secret name | Purpose | Current disposition |
|---|---|---|
| `V2_ORDINARY_AUTHORIZER_PRIVATE_KEY` | Sign only canonical ordinary-v2 settlement intent | New dedicated key; absent |
| `V2_ORDINARY_APP_ID` | Select the repository-scoped ordinary-v2 App | New dedicated App; absent |
| `V2_ORDINARY_APP_PRIVATE_KEY` | Mint its short-lived installation token | New dedicated key; absent |

The v1 admission and callable isolated-operation credentials are outside this inventory and must not be
copied, renamed or accepted as substitutes. Public SPKI/App identities, the trust anchor, installation id,
permission ceiling, rotation owner and revocation procedure remain pending until milestone 04.

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
- v1 genesis and `OpenV2` remain unchanged. Provisioning, anchor enrollment, installed rehearsal and
  receiver activation each retain their later explicit evidence boundary.

The reviewer can request source repair. The reviewer does not hold credentials, approve as another person,
or authorize a protected effect.
