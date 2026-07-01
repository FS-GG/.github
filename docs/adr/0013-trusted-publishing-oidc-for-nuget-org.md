# ADR-0013: Publish to nuget.org via Trusted Publishing (OIDC), not a long-lived API key

- **Status:** Accepted
- **Date:** 2026-07-01
- **Affects:** `.github` (admin provisioning, registry, this ADR), FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance (producer release workflows)
- **Supersedes:** [ADR-0012](0012-dual-publish-to-nuget-org.md) §6 (the admin-gate mechanism) and its §4 push authentication. ADR-0012's decision to **dual-publish to nuget.org** (§1–§5) stands unchanged — this ADR only changes **how a producer authenticates the push**.

## Context

ADR-0012 chose to dual-publish every FS-GG package to public nuget.org and specified the
auth mechanism as a **long-lived push key** stored as the org secret `NUGET_ORG_API_KEY`,
with a fail-closed guardrail modelled on `.github#21` (the GitHub-feed App). Issue #103
tracks that admin gate; `.github#104` shipped the code half as a reusable cross-repo
workflow `FS-GG/.github/.github/workflows/nuget-org-push.yml`.

nuget.org now offers **Trusted Publishing** — OIDC-federated, keyless publishing. A
GitHub Actions job with `id-token: write` mints a short-lived GitHub OIDC token; the
`NuGet/login@v1` action exchanges it at nuget.org for a **single-use API key valid ~1 hour**,
used for that one `dotnet nuget push`. nuget.org validates the token against a
**Trusted Publishing policy** (owner + repository-owner + repository + workflow-file [+ optional
environment]) that the package owner registers in the nuget.org UI. This removes the
long-lived secret entirely — nothing to store, rotate, or leak — matching the OpenSSF
"trusted publishers for all package repositories" direction.

**The decisive constraint — reusable workflows don't work.** nuget.org matches the trust
policy against the repo/workflow **where the OIDC token is minted** (where `NuGet/login`
runs). When that step lives in a **cross-repo reusable workflow** (our
`FS-GG/.github` `nuget-org-push.yml`, called via `workflow_call` from a producer), the
exchange fails `401 "No matching trust policy"` — an open, unresolved defect
([NuGet/login#6](https://github.com/NuGet/login/issues/6)). The documented reliable pattern
is to keep **login + push in each producer's own workflow file**. So the centralized
reusable workflow that `.github#104` added is the **wrong shape** for trusted publishing and
is retired here.

## Decision

1. **Authenticate nuget.org pushes with Trusted Publishing (OIDC), not `NUGET_ORG_API_KEY`.**
   Each producer's release job requests `id-token: write`, runs `NuGet/login@v1` to obtain
   a short-lived key immediately before the push, then `dotnet nuget push … --api-key
   ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
   --skip-duplicate`. No long-lived push secret exists anywhere.

2. **Login + push live in each producer repo's own release workflow** (SDD, Rendering,
   Governance) — never in a cross-repo reusable workflow (NuGet/login#6). This is the only
   deviation from ADR-0012's "reusable guardrail" shape; everything else in ADR-0012 (§1
   additive, §2 scope, §3 byte-identical/no-repack, §4 gated ordering org-feed-first,
   §5 listing metadata) is unchanged.

3. **Retire the reusable workflow.** Delete `FS-GG/.github/.github/workflows/nuget-org-push.yml`
   (from `.github#104`). The `publishing-and-deployment` skill documents the inline
   trusted-publishing snippet instead of a `uses:` call.

4. **The admin gate is a set of Trusted Publishing policies, not a secret.** An org-admin,
   signed in to nuget.org as the FS-GG-org owner, creates **one policy per producer repo**
   (Repository Owner `FS-GG`; Repository `FS.GG.SDD` / `FS.GG.Rendering` / `FS.GG.Governance`;
   Workflow File = that repo's release workflow filename only, e.g. `release.yml`). The
   `FS.GG.` **ID-prefix reservation stays required** (anti-squat) — trusted publishing
   replaces the *key*, not the prefix reservation. Optionally store the nuget.org **profile
   name** (not email) as a non-sensitive secret `NUGET_USER` for `NuGet/login`'s `user` input.

5. **Fail-closed is intrinsic.** With no secret to check, the guardrail is nuget.org itself:
   until a matching policy exists, `NuGet/login` returns `401` and the release fails loud —
   never a silent no-op, never a half-published coherent set (ADR-0012 §6 intent preserved by
   a different mechanism). The org GitHub Packages feed remains authoritative; a failed
   nuget.org push is retry-safe (`--skip-duplicate`).

## Consequences

- **`.github` (this repo):** records this ADR; retires `nuget-org-push.yml`; updates the
  `publishing-and-deployment` skill (+ its `.agents` mirror, ADR-0011) to the inline snippet;
  re-annotates the `nuget-org-published` registry entry + its `docs/registry/compatibility.md`
  projection (admin gate → per-producer policies + `NUGET_USER`; drop `NUGET_ORG_API_KEY`).
  **Admin task (#103, revised):** create the three Trusted Publishing policies, reserve the
  `FS.GG.` prefix, optionally set `NUGET_USER`. No org push secret to mint.
- **FS.GG.SDD / FS.GG.Rendering / FS.GG.Governance:** each release workflow adds the
  `id-token: write` + `NuGet/login@v1` + push steps in its **own** `release.yml` (not a
  `uses:` of a shared workflow), plus the ADR-0012 §5 listing metadata.
- **Rollout caveat:** Trusted Publishing is rolled out gradually and a policy on a **private**
  repo starts in a 7-day pending-activation window until the first successful publish supplies
  the repo/owner IDs (resurrection-attack guard). The FS.GG.* producer repos are public, so
  policies activate on first publish.
- **Reversibility:** the nuget.org **IDs** remain permanent (ADR-0012 §Consequences, ADR-0003).
  The *auth mechanism* is reversible — a policy can be deleted and a long-lived key re-adopted
  — but there is no reason to; trusted publishing strictly removes secret-handling risk.
- **`nuget-org-published` coherence:** stays `coherent: false` until every in-scope package
  resolves on nuget.org at its current version; the blocker is now "policies created" rather
  than "secret added".

<!-- Follow-up: reconcile docs/architecture.md (feed/distribution picture) once the registry
`nuget-org-published` entry reflects trusted publishing and the first package resolves on
nuget.org. Same follow-up ADR-0012 already carries. -->
