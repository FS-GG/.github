# FS.GG.Coordination protected receipt approval report

**Observed:** 2026-08-27T10:08:36+02:00

**Repository:** [`FS-GG/FS.GG.Coordination`](https://github.com/FS-GG/FS.GG.Coordination)

This supplements, without changing, the fingerprinted
[administrator settings report](2026-08-26-fs-gg-coordination-admin-settings-report.md).
The protected epoch and accepted receipts remain authority; this report is an operator projection.

## Verified post-state

Organization configuration 17 is attached to repository id `1346720714`; its association is `attached`;
CodeQL default setup is configured for Actions; and private vulnerability reporting is enabled. The organization's
current entitlement leaves the repository's effective validity-check and non-provider-pattern projections disabled
even though configuration 17 requests them. They are recorded as license-unsupported, not operational.
Configuration 17's distinct generic-secrets value remains `not_set`.

The reviewed repository rulesets are active:

- `release-tags-immutable`, id `21633398`, protects `refs/tags/v*` with no bypass, required signatures, and
  deletion/update/non-fast-forward protection;
- `main-protected`, id `21633423`, protects the default branch with no bypass, squash-only pull requests, one
  current approval, stale-review dismissal, last-push approval, CODEOWNERS review, resolved threads, and the six
  exact strict GitHub Actions checks.

## Required administrator action

Team `coordination-maintainers` currently has only `EHotwagner`, the author of the protected receipt PR, so no
different CODEOWNER can approve it. Add existing FS-GG member and repository Admin `nuklearwanze` at
[Organization → Teams → coordination-maintainers → Members](https://github.com/orgs/FS-GG/teams/coordination-maintainers),
then have `nuklearwanze` approve
[FS-GG/FS.GG.Coordination#23](https://github.com/FS-GG/FS.GG.Coordination/pull/23).

Do not weaken the ruleset, add a bypass, dismiss the required checks, or use the author account for this approval.
The approval must target the current PR head after the independent receipt critic passes.

PR #23 binds a new idempotent verification batch because the first batch's historical pre-state bytes were not
retained. It captures canonical protected pre-state bytes, reapplies the unchanged tag ruleset followed by the
unchanged branch ruleset, and captures a fresh post-state receipt. This supersedes the earlier unaccepted batch;
it does not reconstruct missing historical bytes.
