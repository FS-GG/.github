# UTEL-HREL-01 — Independent telemetry Host 0.1.2

Owner: `.github` Host package and release producer. Main/SystemAdmin owns the protected installation and post-adoption readback.

The coherent CLI 0.91.1 release contains the corrected completion reducer for producer tooling, but the running `FS.GG.Telemetry.Host` 0.1.1 embeds its own earlier copy through `FS.GG.Telemetry.Store`. Host 0.1.2 is an independent one-package patch. It must be published under `telemetry-host/v0.1.2` with its original `.nupkg`, `manifest.json`, and `publication-journal.json` before Main's updater can select it.

## Source and candidate

The Host version is 0.1.2; its package notes name the native completion fix. The read-only `release-telemetry-host-successor-candidate.yml` requires an exact current-main workflow dispatch, unused 0.1.2 tag and both-feed version, locked Host/browser/projection tests, one Release package, installed tool qualification, and a source-bound `fsgg.telemetry.host-release/1` manifest. It retains the exact package and manifest as an Actions artifact. Candidate qualification creates no tag, release, journal or feed effect.

## Publisher contract

The retired `release-telemetry-host.yml` remains source qualification only. A distinct successor publisher must independently authenticate the candidate run and artifact identity, outer archive digest, exact safe members, manifest and package hashes. Its authority is the current main-branch first-attempt publisher run by the sole operator, freshly read from native Actions and main-ref APIs before every journal or remote effect. A new protected journal ref under `fsgg/v2/journal/release/` binds source, version, candidate archive digest and operator; the active authority-repository rulesets admit only the ordinary ledger App for journal creation and update.

The effect order is tag, draft release, GitHub Packages, nuget.org, original package asset, manifest asset, publication-journal asset and promotion. Each intent is committed before dispatch. A response is never settlement: a fresh provider readback must match the prepared payload or asset digest before journal verification. A delayed 404 after a dispatch leaves intent pending for readback; it does not authorize blind replay or repacking. Publisher preflight is read-only and must pass on the same exact main SHA and candidate before publication.

The new publisher workflow filename, environment, owner and package ID require a matching nuget.org Trusted Publishing registration before it can exchange an OIDC identity. That owner action is an explicit prerequisite to feed effects. Publication must fail before the GitHub Packages write if it is missing. Do not change the retired workflow to reuse its prior registration.

## Completion

Verify journal terminal state, exact release assets, both feeds' normalized producer payloads, clean public-feed Host tool installation and the updater's expected three-asset shape. Then send Main the immutable tag, source SHA, manifest and original archive digest. Main selects the Host release with its protected updater, verifies image and five-minute health, and reports private delivered/completed and public eligible counts. A concrete public alias requires separate approval before public completed rows appear.
