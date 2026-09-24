# Ordinary-v2 dedicated custody enrollment

This packet prepares the one-time enrollment for V2-CI-I1. The ordinary post-merge workflow remains
inactive until the pinned Coordination installer, public anchor, dedicated keys, and isolated hosted
qualification all pass. Enrollment does not authorize `OpenV2` or change the current v1 admission gate.

## Exact App registration

The dedicated App is **FS-GG Ordinary V2 Settlement**, owned by FS-GG. Its homepage is the
[Coordination repository](https://github.com/FS-GG/FS.GG.Coordination). It is owner-only, has no webhook,
no subscribed events, no organization permissions, and only repository **Contents: read and write**
(Metadata: read is implicit). The operator can use this
[preconfigured organization registration form](https://github.com/organizations/FS-GG/settings/apps/new?name=FS-GG+Ordinary+V2+Settlement&description=Dedicated+contents+writer+for+unattended+ordinary-v2+settlement+in+FS-GG+Authority&url=https%3A%2F%2Fgithub.com%2FFS-GG%2FFS.GG.Coordination&public=false&webhook_active=false&contents=write),
then verify the displayed permissions before creating it. GitHub App registration itself requires the
authenticated browser form; a repository API token cannot perform that form step.

Install it with **Only select repositories**, selecting just
`FS-GG/FS.GG.Coordination.Authority` (repository ID `1351660651`). Do not install it on `.github`, other
repositories or all repositories. The operation token must name only repository ID `1351660651` and
request `contents:write` (with implicit `metadata:read`). Source, PR, check and workflow reads use the
trusted workflow's separate read-only `GITHUB_TOKEN`; this App needs no Administration, Checks, Pull
requests, Actions, Workflows, repository creation or organization grant. The journal Git object/ref
endpoints require Contents write; token requests must explicitly narrow both repository ID and
permission even though the App installation itself is already selected-repository.

The Authority repository's live `v2-journal-writer` ruleset (`21872113`) protects
`refs/heads/fsgg/v2/journal/**/*` with creation/update rules and currently lists only incumbent App
`4882140` as an always-bypass actor. After the new App ID is known, add **that exact App ID** as one
more bypass actor for this writer ruleset, then independently read back its scope and actors. Retain the
incumbent until its separate governed retirement. The `v2-journal-integrity` ruleset (`21872115`)
protects the same journal population against deletion/non-fast-forward and has **zero** bypass actors;
do not grant the new App an integrity bypass or weaken either rule. The credential job must refuse when
the live writer/integrity ruleset differs from the accepted public binding. This settings update is a
one-time protected setup effect, not part of ordinary settlement permission.

## Dedicated private material and public anchor

Provision these three **new** values directly into the `.github` Actions `ordinary-v2` environment.
That environment has a custom `main` branch policy and zero required reviewers. Its shell was read back
on 2026-09-24 with zero secrets; this is setup only.

| Environment secret | Value | Public counterpart |
|---|---|---|
| `V2_ORDINARY_APP_ID` | Numeric ID of the new App | Same ID in the anchor |
| `V2_ORDINARY_APP_PRIVATE_KEY` | New App private key generated in GitHub App settings | App ID and installation ID in the anchor |
| `V2_ORDINARY_AUTHORIZER_PRIVATE_KEY` | New RSA private key used only for canonical RSA-PSS/SHA-256 intents | Key ID and SHA-256 of DER SubjectPublicKeyInfo in the anchor |

The App private key and authorizer private key must travel from their creation point straight to the
environment secret input. They must not pass through fdev, an agent transcript, a PR, an artifact, a
shell trace, or a local test fixture. The one-time custodian may generate the RSA key on a trusted
machine, then compute the public digest:

```console
umask 077
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out ordinary-v2-authorizer.pem
openssl pkey -in ordinary-v2-authorizer.pem -pubout -outform DER | openssl dgst -sha256
```

Only the chosen public key ID and resulting 64-hex digest belong in the anchor or a handoff. Load
the PEM into the environment secret directly and securely remove the local copy according to the
custodian's key policy. The installed job recomputes the digest
from the private key's public component before signing and refuses a mismatch. It exposes neither a
general-purpose signing API nor a token to an agent.

Create `policy/v2-ci-ordinary-settlement-anchor.json` only after the identities are known and
independently read back. Its exact schema is `fsgg.github.v2-ci-ordinary-settlement-anchor/1` with:

```json
{
  "schema": "fsgg.github.v2-ci-ordinary-settlement-anchor/1",
  "policyId": "v2-ci-i1-ordinary-settlement-v1",
  "operationClass": "ordinary-post-merge-delivery-settlement",
  "authorizer": {
    "keyId": "<new-public-key-id>",
    "algorithm": "RSA-PSS-SHA256",
    "publicKeySpkiSha256": "<64-lowercase-hex-public-digest>"
  },
  "writer": {
    "appId": 0,
    "installationId": 0,
    "repository": "FS-GG/FS.GG.Coordination.Authority",
    "repositoryId": 1351660651,
    "permissions": {"contents": "write", "metadata": "read"}
  },
  "acceptedAt": "<UTC-time-after-readback>",
  "sourceCommit": "<exact-protected-policy-commit>"
}
```

The displayed zeros and angle-bracket values are explanatory placeholders, not accepted data. The
installed validator must reject an absent anchor, nonpositive IDs, unsupported key, changed policy or
wrong repository/permission. Capture the App ID and installation ID from GitHub's own settings/API
readback, not from a typed operator claim. Record only public identities in the repository. The
environment secret inventory remains separate and never records secret values.

## Activation, rotation and revocation

Publication must pin one immutable Coordination artifact and its content digest in the protected workflow.
First run the installed synthetic matrix with the new App on explicitly bounded non-production Authority
refs and a synthetic `OpenV2` epoch. The actual production `OpenV2` state remains a separate gate.
Only after this installed readback and the public anchor are accepted may a source PR set
`credentialJob.installed` to `true`. The normal main-push route then has no per-run human reviewer or
host credential session.

For rotation, disable activation, enroll the new App key or authorizer key directly in the environment,
publish its public identity, qualify the exact new installer/workflow/policy bytes, and reconcile original
attempts before revoking an old signer needed for replay. A changed key without matching accepted public
anchor refuses. For urgent revocation, disable activation and remove the environment secret or uninstall
the dedicated App; leave unknown journal outcomes pending for native reconciliation. Do not reuse v1 or
callable-isolated keys as a fallback.

GitHub's [App registration guidance](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app),
[App token scoping](https://docs.github.com/en/rest/apps/apps#create-an-installation-access-token-for-an-app),
and [Git ref permissions](https://docs.github.com/en/rest/git/refs) describe the external provider steps
and permission ceiling used here.

## Separate installed rehearsal custody

The isolated hosted matrix uses a distinct public
[`FS-GG/FS.GG.Coordination.Authority.Sandbox`](https://github.com/FS-GG/FS.GG.Coordination.Authority.Sandbox)
repository, ID `1385801070`, seeded at `fe6292e9fa729a57cebbe4c8cf4d51dcad05500c`.
Its active journal writer ruleset is `23947019` (creation/update, zero bypass until enrollment) and its
integrity ruleset is `23947025` (deletion/non-fast-forward, zero bypass). The `.github`
`ordinary-v2-rehearsal` environment, ID `22669445419`, has custom `main` branch policy `60924371`,
zero required reviewers and zero secrets. These are inert setup shells, not successful operations.

Rehearsal must use a **second** dedicated App, authorizer key and environment secret set. The
[preconfigured rehearsal registration form](https://github.com/organizations/FS-GG/settings/apps/new?name=FS-GG+Ordinary+V2+Rehearsal&description=Isolated+ordinary-v2+settlement+qualification+writer&url=https%3A%2F%2Fgithub.com%2FFS-GG%2FFS.GG.Coordination.Authority.Sandbox&public=false&webhook_active=false&contents=write)
requests only Contents write, no webhook and no organization permissions. Install it only on sandbox
repository ID `1385801070`. Put its new private material directly into `ordinary-v2-rehearsal` as
`V2_ORDINARY_REHEARSAL_APP_ID`, `V2_ORDINARY_REHEARSAL_APP_PRIVATE_KEY` and
`V2_ORDINARY_REHEARSAL_AUTHORIZER_PRIVATE_KEY`; publish only the App/installation IDs and a fresh
authorizer key ID/SPKI digest. Add only that App ID as the sandbox writer-ruleset bypass, retaining
zero integrity bypass. The rehearsal profile and anchor must be separately pinned and reject the
production repository ID, App, environment and real epoch. The production profile must reject the
rehearsal repository and synthetic epoch before signing.

Using the production App in this sandbox would let rehearsal code holding its private key mint a
production Authority token, even if one token request was scoped to the sandbox. The two App
installations and private keys therefore stay disjoint.
