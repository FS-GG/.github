# Standalone telemetry P1 publication-migration evidence

Date: 2026-09-10. Status: accepted Main evidence; P1 complete.

This report records the source, failed-first-attempt recovery, hardened-system-unit
qualification, generation-2 cutover, remote publication readback, and
ambiguous-response recovery used to accept P1. It contains no credential,
authorization header, raw private telemetry, or private dashboard data.

## Accepted source and qualification

The handoff-only publisher and atomic aggregate source first landed in
[`.github` PR #3407](https://github.com/FS-GG/.github/pull/3407), merge
`5d036dc3ef307a72d8f7410719f5aaf75ea267ff`. Its first SystemAdmin consumer
landed in [PR #9](https://github.com/EHotwagner/SystemAdmin/pull/9), merge
`5c6c20894f9435e6ba8a9da14fce57ddc5a3dd84`. The retry-proof correction landed
in [`.github` PR #3417](https://github.com/FS-GG/.github/pull/3417), exact head
`04de23af0420f341b516bce23a03abc51a8f7893`, merge
`81115b385af5b269d08661f039b995414ae7102e`. All PR checks passed, including a
successful rerun after one transient board-reconcile HTTP 503.

The correction preserves the immutable generation-1 proof and permits only a
consecutive generation whose proof binds fresh source, configuration, unit,
candidate, labels, approval, and remote-baseline digests. Retry additionally
requires the earlier failure to be terminal and rolled back, no replacement
GitHub mutation or ambiguous intent, the incumbent activation restored
byte-for-byte, and healthy incumbent recurrence.

| Claim | Verification |
|---|---|
| The accepted publisher candidate is the corrected `.github` source. | Installed candidate SHA-256 `2b0e84e58ac2a315e6ac4efe5413ce77a318bdda0074f96e7e6c0dbf212e10bc`; PR #3417 ran 58 focused tests, the numerical two-UID proof, routine eligibility, 59 claim-generation fixtures, Python compilation, and diff checking. |
| The SystemAdmin retry coordinator and hardened SYSTEM units are accepted source. | [PR #24](https://github.com/EHotwagner/SystemAdmin/pull/24) merge `00498eae59334dc7cfcbaea9c1277b8ca6058b26`; self-pin PR #25 merge `ff061b606cb826b142eb96bead8a9fe3deb0319d`; configuration migration PR #26 merge `eb7ed03bc8b3dd4d59733a74d9b7ad2e56def690`; corrected byte-pin PR #27 merge `b8d50d8c2e2fe9c872025092051de8826338b74c`. Each required `source-qualification` check passed. |
| The SYSTEM service retains the cross-UID handoff boundary. | Fifteen Python fixtures passed as root and non-root, including numeric UID separation, deterministic user-namespace overflow/refusal, and a system-boundary `2750 61201:61203` read/no-write probe; exact upstream generation-2 proof and activation fixtures, `py_compile`, diff check, and rendered `systemd-analyze verify` also passed. Host ShellCheck was unavailable; the required GitHub source-qualification check ran ShellCheck successfully. |

## First attempt and rollback

The first live attempt failed closed before GitHub mutation with
`HANDOFF_OUTGOING_UNSAFE`. In the hardened user unit, `ProtectSystem=strict`
created a user/mount namespace that presented the producer-owned handoff
UID:GID `954:953` and supplementary GID `953` as overflow ID `65534`. Outside
that namespace the handoff was correctly `2750 954:953`. The closed numeric
validator correctly refused this incompatible execution boundary.

The replacement remained disabled and inactive, its activation was archived,
and the original incumbent activation was restored byte-identically. The
incumbent returned enabled and active and published remote commit
`feee7ce5a35be2821a883c34bbe01adef5751ff5`. There was no replacement GitHub
mutation or ambiguous intent. The first proof remains at
`/var/lib/fs-gg/telemetry-dashboard-cutover-proof/cutover.json`; it was neither
deleted nor overwritten.

| Claim | Verification |
|---|---|
| Generation 1 is an immutable failed-and-rolled-back attempt. | Evidence digest `b37a8d1a976d4f9065b347b6ae81c9cd9d5f6be452e7a3d014b4b9c2b7c3ac7c`; proof-file SHA-256 `49bc910b78c5269e35d2f992a135ac9bffca322f256f84d9199ae2f46d0bed44`. |
| The incumbent activation was restored byte-for-byte. | Before and restored activation SHA-256 `ce5bc883f2a2b6f6f8c497702680d8d4a17ddd1685926389650fc1fd180cf743`; archived replacement activation SHA-256 `cb9aabf8e6f1d1422724a01fb3c52e3a8dca73a664e7e4f4780b188ef1ee0859`. |

## Generation-2 preview and cutover

The effect-free preview was bound to remote baseline
`a615753e6a493ff07175dcde59449ecff55713b9` and snapshot
`90af3211ec60002ae1e342808437d3cf7d16a1429acb4f9072a87eb2c004c351`.
It reported no effects, preserved the incumbent as the sole active recurrence,
and kept the replacement disabled. The accepted authorization covered only
that byte-identical preview.

| Bound input or preview | Verification |
|---|---|
| Reviewed configuration | File SHA-256 `cee2f2773d4067f7ceab22bd237e93a60cdc29d057835453f6f2f3508f3d3d1f`; canonical digest `6a14d25f635b2893daab9d70a50b69b6902b69f8399a666a3e2cebf5a59cef0b`. The prior reviewed file remains mode 0400 with SHA-256 `3927a54e980d431f2f7c9c879d6a8586d74dd6d2025d6a17c444f04cfbf81c71`. |
| Coordinator, SYSTEM unit, labels, and staged aggregate | SHA-256 respectively `4917823375d3639b00c2b309c9b7a8048830bf20e5c0dc64fcf923911cbe50e2`, `dbda678b058409bd50684bc4e6931803c182a916d16834530ffc0d9cf904b9ce`, `fc607e4bced90516c3bd67791834c255f242e42d9cc13bd9e7c6b7dde6c2c3a9`, and `5138c9a872447ecc580be4b41e58b39edb57de4ab16504baa1a1acb2c22574f6`. |
| Effect-free preview | `/var/lib/fs-gg/telemetry-dashboard-cutover-proof-2/proof-preview.json`, SHA-256 `3f52224bcc3ba6771bbb95026fc4c22f9d49f8d5fbd555cf90570d1e11f36c22`, `effects: []`. Preparation readback: `/var/lib/fs-gg/telemetry-dashboard-cutover-proof-2/preparation-readback-5138c9a872447ecc580be4b41e58b39edb57de4ab16504baa1a1acb2c22574f6-90af3211ec60002ae1e342808437d3cf7d16a1429acb4f9072a87eb2c004c351.json`. |
| Real Main execution identity and access | Transient SYSTEM-unit probe ran as UID 953 with groups `[952,953]`; it observed the handoff as `954:953/2750`, read it successfully, and could not write it. |

The authorized generation-2 cutover completed at the fixed destination
`FS-GG/.github`, ref `telemetry-data`, path `host.json`. It disabled and stopped
the incumbent user recurrence, removed its event activation, recorded the
replacement activation, and enabled the hardened SYSTEM timer. The replacement
timer is enabled and active; its oneshot is healthy and inactive between runs.
The backup timer is disabled. The incumbent timer is disabled and inactive, its
service is inactive, and its event activation is absent. This is exactly one
publication recurrence.

The new proof is
`/var/lib/fs-gg/telemetry-dashboard-cutover-proof-2/cutover.json`, with evidence
digest `c471d14fb6c19b5ff35940f1e1a95fa06b7dba26fefc625623885a95eb787b0f`,
file SHA-256 `12030e65043a4b5d94cfb73eaae20a3670411a174d984779a329d2bc84cdd180`,
and result SHA-256
`1166e668b910fdcc0a3c2dbcdb262b277fc215d4d2493dc2f9fc9f5bd2fc4380`.
The replacement activation SHA-256 is
`561070bdb1fddd0e95486d319bdeb0f5f5ab245da89195ffc208532aef122b6f`.

## Live publication and recovery readback

The first replacement publication completed at `2026-09-10T18:39:13Z` as
commit `3ef8b06455a1f1a0ee73731d22475a37cde9e81a`, exactly one commit after the
authorized baseline. Its `host.json` is 2479 bytes, Git blob
`1909a0c0b7c8d075b383782817ce778b916926b5`, and SHA-256
`5138c9a872447ecc580be4b41e58b39edb57de4ab16504baa1a1acb2c22574f6`.
The closed public document reports revision
`582cd4649638fecf110d9f8add060905144f63856e40224fdc07701f427d28e0`
and totals of 67 facts, zero usage, and zero delivery.

| Claim | Verification |
|---|---|
| Remote publication is the authorized aggregate and has the expected ancestry. | Native GitHub ref/path readback returned commit `3ef8b06455a1f1a0ee73731d22475a37cde9e81a`, parent baseline `a615753e6a493ff07175dcde59449ecff55713b9`, blob `1909a0c0b7c8d075b383782817ce778b916926b5`, 2479 bytes, and matching SHA-256 `5138c9a872447ecc580be4b41e58b39edb57de4ab16504baa1a1acb2c22574f6`. |
| Ambiguous response/restart recovery created no duplicate publication. | The operator reconstructed the exact persisted post-push intent and started a fresh SYSTEM service process; it returned `status=reconciled`, left the remote at `3ef8b06455a1f1a0ee73731d22475a37cde9e81a`, created no second commit, and cleared the intent. A later scheduled run also left the bytes unchanged. |
| Last-good state matches remote readback. | Last-success SHA-256 `97c74203e3c8ce40f52102740112cd39c762378382234e0d26007ff04eebef31` binds the same commit, snapshot digest, and public revision. |
| Publisher and producer credentials remain separated. | Native account probes found that the producer cannot read the publisher credential and the publisher cannot read the producer configuration or incumbent private configuration. The private dashboard needs no publication credential. |
| Telemetry ingestion was preserved through cutover. | `fdev-telemetry health` returned ready, and the Host conmon remained PID 1733280 with original local start `2026-09-10 16:46:42`. |

The complete private Main report is
`/var/lib/fs-gg/telemetry-dashboard-cutover-proof-2/main-cutover-report.json`,
SHA-256 `955245a518d55844fb86465c11ff4e89eb3a5199e836225cb8c813157b612c49`.
Rollback was not needed; generation 1 and its archived rollback artifacts remain
unchanged.

## P1 exit assessment and limitations

P1 is accepted. A single authorized publisher now operates through the
separate hardened SYSTEM service account and fixed destination. The cutover
validated the atomic aggregate handoff, closed labels and approval binding,
fixed credential identity, last-good retention, exact remote readback, and
duplicate-free response-loss/restart reconciliation before retiring the
incumbent recurrence.

The response-loss exercise reconstructed the exact persisted state after a
successful push; it did not deliberately interrupt the network. The isolated
publisher currently uses the existing `EHotwagner` OAuth token, whose observed
scopes are broader than destination-only contents write. These limitations are
retained and are not reclassified by the successful cutover.
