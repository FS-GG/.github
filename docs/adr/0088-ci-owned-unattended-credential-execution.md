# ADR-0088: CI owns unattended credential execution for ordinary v2 operations

- **Status:** Accepted
- **Date:** 2026-09-24
- **Decision owner:** FS-GG accountable programme owner
- **Affects:** `.github` trusted CI/credential policy and `FS.GG.Coordination` operation installer
- **Activation:** Prospective only; no credential route activated
- **Applies:** [ADR-0079](0079-single-accountable-delivery-authority.md) to a future ordinary-v2 credential path; [ADR-0080](0080-scoped-child-qualification-comprehensive-milestone-closure.md) and [ADR-0084](0084-semantic-reuse-never-cancels-coherent-validation.md) still govern qualification

## Context

The one-time v1 admission experience exposes a manual agent-to-host custody relay after deterministic
qualification. It adds agent commands and status copying beside sensitive credentials without supplying a
second human authority. Development is siloed in a rootless fdev Podman container; the host must not be an
interactive signer, token issuer or installer on the future normal path. The programme wants zero routine
human interaction and the existing narrow 10% bureaucracy ceiling, while preserving technical verification
and optimizing CI latency. A separate reviewer subagent can critique source but, when it shares fdev's
environment, is not a distinct security principal.

## Decision

For a **future qualified ordinary-v2 class**, credential use belongs in one trusted, post-merge
operation path: a secret-free qualification job emits an exact receipt, then a credential-bearing job
rechecks current authority, performs one scoped effect and independently reads it back. Secret-bearing
execution is never part of PR-head CI or an agent session. It executes on an ephemeral remote CI runner,
not a self-hosted runner on Main, Work or the fdev host; routine operation must succeed without any
host-side wallet, DBus/Secret Service, SSH agent, socket or agent conversation. The selected credential
store and trigger must be versioned and tested before activation. New v2 authorizer/App keys live
as `.github` Actions environment secrets; current v1 wallet keys are not copied or implicitly
reused. The public v2 identities/trust anchor require separate accepted enrollment. A distinct machine-only ordinary-v2
environment has no per-run human reviewer and is restricted to the trusted protected-main route; it
does not replace the current v1 admission or cutover environments. A checklist-driven reviewer
subagent owns critique and public custody-policy findings, but not
raw key access, token issuance, an additional approval identity or a per-operation veto. Its review may be
selected for material credential changes or sampling; deterministic gates remain mandatory.

The trusted-writer assumption is explicit: rootless Podman isolates the development process from host
credential custody, but this design does not prevent a compromised authorized fdev writer or remote
credential runner from abusing its own authority. The keys are not directly extractable from
the fdev container, but code in the remote secret-bearing job can read them, and a compromised
trusted writer may be able to change that code. A stronger adversarial boundary would require
independent identity/approval isolation and a separate policy decision. Safety comes from exact source and
policy binding, limited operation classes, scoped/short-lived credentials, one-attempt CAS, refusal controls
and independent readback—not from calling one agent a second party. Routine execution has no human prompt;
failures and unknown outcomes stop and escalate instead of retrying by conversation.

This decision is **prospective**. It does not amend the one-time v1 genesis policy of
[ADR-0087](0087-single-owner-v1-admission-genesis-approval.md), authorize a production write, or remove the
currently specified human `OpenV2` decision in GS2-13. Replacing either current protected gate requires an
explicit operation-specific policy amendment and independently qualified controls before that gate changes.
The [V2-CI-I1 design](../coordination/2026-09-24-v2-unattended-ci-credential-interlude.md) owns the bounded
implementation plan and acceptance.

## Consequences

The normal future v2 operation no longer needs a second host agent or a credential relay. CI becomes a
credential-bearing component whose secret scope, source trust, runner isolation, rotation, revocation and
failure settlement must be qualified. The interlude may proceed beside GS2-09 source work, but its selected
receiver profile must be installed before GS2-10 freezes the candidate or be explicitly deferred. The
existing bureaucracy and CI-performance measures apply; no new per-PR form or approval cycle is created.

## Alternatives considered

- Keep manual Main-agent custody orchestration: adequate for a one-time protected genesis, but an
  unnecessary recurring agent/credential boundary and administrative critical path for v2.
- Give a reviewer subagent the raw keys: no independent authority when it shares fdev's tools, and a larger
  secret/prompt surface than a narrowly scoped CI job.
- Add a permanent host broker: potentially useful under an adversarial container threat model, but no such
  independent approval/isolation policy exists here and the service would add a new privileged surface.
- Expose secrets in ordinary PR CI: rejected because unreviewed source or PR-controlled artifacts could
  execute with credentials.
