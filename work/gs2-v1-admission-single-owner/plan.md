# Single-owner v1 admission authorization

The accountable owner has explicitly accepted replacing the unavailable second-person approval for the one-time v1 admission genesis. This narrows the change to a new environment; `fleet-cutover` and its existing non-self-review policy remain unchanged. The admission journal and ordinary production mutation fence remain untouched.

## Formal constraint

`FS.GG.Coordination.Protocol/Protocol.md` models the durable v1 admission journal, exact-parent CAS, and effect fences, but no GitHub reviewer roster. It remains unchanged. The relevant canonical gates are `protocolEnvelopesAreValidAndOrdered`, `durableProtocolCheckpointsArePreserved`, `mutationResultsAreBound`, `uncertainMutationOutcomesStayUnknown`, and `durablePlansAreOrderedAndResumable`.

## Steps and gates

1. **Installed and read back:** `.github` environment `fleet-v1-admission-owner` has ID `22582241959`, sole reviewer user ID `1645484`, `prevent_self_review=false`, five-minute wait, and exactly one custom branch policy `main`. The previous `fleet-cutover` environment remains unchanged. Desired configuration is `environment.json`.
2. **Source complete:** only `gs2-v1-admission-protected-authorization.yml` uses the new environment and emits receipt `/2`. Manual main-only dispatch, first-attempt check, read-only permissions, exact public inputs, pinned artifact action and short expiry remain. The workflow SHA-256 is `7193f2b3636984b25bd92f5ea19c41cc7d1a855425454d32d69ef65782506820`; focused workflow tests pass. The unchanged Quint gate is pending.
3. **Source complete, focused checks pass:** native evidence `/2` requires exact new environment rules, sole owner as run actor and approver, one native approval, exact artifact and workflow binding. Fake-GitHub controls, the full 470-test unit suite, Q6, and the warning-as-error solution build pass. The unchanged Quint gate is pending.
4. Record the cross-repo decision in ADR-0087 and update the two draft PRs with live evidence. Gate: warning-as-error solution build, full unit/architecture suites, canonical Quint qualification, workflow CI, and fresh live environment readback.

Stop before dispatch or Authority writes until the source and workflow are merged, the protected App credential and trust material are securely provisioned, and the remaining live read/write adapter gates pass. Any failed formal invariant sends this plan back to review; the spec is not weakened to satisfy implementation.
