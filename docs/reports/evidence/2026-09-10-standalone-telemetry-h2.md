# Standalone telemetry H2 release and deployment evidence

Date: 2026-09-10. Scope: H2 source, release and isolated deployment evidence
for the optional standalone telemetry host. Main activation remains a separate
H3 operation.

## Delivered source boundary

The initial package provided `FS.GG.Telemetry.Host` 0.1.0 as an independently
versioned Linux x64 .NET tool; the accepted shared Dashboard/Store corrections
ship as 0.1.1. It includes the Host, Contracts, Store, private
Dashboard projection and fixed assets, and the measured Akka/SQLite runtime
closure. It does not add Host or Akka to the workspace coordination CLI package
and is not a member of its three-package coherent release set.

The installed command implements explicit offline `init`, `enroll-producer`,
`preflight`, `status`, `backup`, and `restore` operations plus migration-free
`serve`. Production execution always uses the existing storage assessor. The
local container's overlay placement is refused and is recorded as unqualified,
so local process tests make no production durability claim.

The browser surface uses separately provisioned random access keys. Only their
SHA-256 hashes are loaded by the host, sessions are bounded by idle and absolute
lifetimes, and each principal carries a closed workspace allowlist. Dashboard
queries accept no database path from the browser. The Store verifies the
requested workspace and complete receipt provenance inside the same read
transaction, using a receipt-linear rowid scan and primary-key lookups. Legacy
unscoped batches cannot enter a provisioned receipt store.

## Recovery boundary

The product backup is a coherent data-only set. It atomically creates a new
directory containing a closed root manifest and one closed workspace backup for
each configured workspace. Workspace backups contain an engine-created SQLite
snapshot and the exact flat pending receipt set. Restore rejects unknown,
duplicate, oversized, missing, corrupt, symlinked, incompatible, or changing
input, validates every staged byte and store invariant, and atomically publishes
only a fresh state root. A backup taken before a later acknowledgement restores
the earlier state; the test observes that the later receipt is unavailable, and
the operator or client must replay the retained post-backup acknowledged input.

The root manifest contains a secret-free digest of logical configuration
metadata; it cannot reconstruct configuration or credentials. Operational
recovery therefore depends on the accepted
[SystemAdmin companion](https://github.com/EHotwagner/SystemAdmin/pull/6) backup of the canonical config
template, TLS material, producer secrets, and browser key-hash files.
The restore rehearsal regenerates config with each `Store.Root` set to
`<fresh-state-root>/<workspaceId>` and passes preflight before cutover. Access
keys themselves must be retained through the private operator channel or
reprovisioned because hashes are one-way.

## Validation observed before the pull request

- Host and remote boundary suite: 15 passed, including real TLS submission,
  restart, duplicate recovery, changed-root restore config, preflight, later
  acknowledgement exclusion, and malformed/open/corrupt restore refusal.
- Dashboard projection suite: 14 passed, including receipt-backed
  pending/applied/rejected state and legacy/scoped shape separation.
- Browser suite: 4 passed; the separately run real Chromium journey exercised
  the genuine TLS Kestrel endpoints.
- Coordination engine suite: 543 passed. The added populated provenance
  regression crossed the 256-row page boundary, observed exactly 300 receipt-key
  computations, and confirmed SQLite uses the integer primary key without a
  temporary ordering tree.
- Local package fixture: 10 passed. A fresh local-only install exposed
  `fsgg-telemetry-host`, matched the reviewed package allowlist, ran without a
  source checkout, and refused the unqualified overlay filesystem without state
  writes.
- Independent release-manifest fixture: 11 passed, including the shared
  `release-saga.py` producer-payload vector, signed-archive distinction, changed
  payload refusal, tag isolation, foreign-journal refusal, and exact resolution
  of both lightweight and annotated remote tags.
- SystemAdmin companion: merged as
  `2f884241d23602a47c70a39cf780a0cf453f96aa`; 18 product-candidate interop and
  operator checks passed, including staged config/credential verification before
  publication. No Main mutation or service activation occurred.

After the deterministic source-path repair, the packaging lane produced equal
payloads from two independent clean roots and prepared a prerelease helper
candidate from source `f66673c5a5babc3034becdf73602c904c96b2355` with archive SHA-256
`8900f71d9cf6ff6a18a290383b492276e72fe29d8f5ac9c955fe0712f53ea5da` and
producer payload SHA-256
`593c8a71e4ed2bc4d84ac0d40e42739d2abbdc284bbd101d1d99d216990ee537`.
Those bytes prove the package path during development; they are not the final PR
artifact or a published release because their source binding predates the final
candidate.

## Separate completion states

- **Source:** the initial Host source was accepted as merge
  `9a2b71389e3dad6fa81426e99f1822a85774fba3`; the shared Dashboard/Store patch
  used by Host 0.1.1 was accepted as
  `431d69d38d71da3b2c293bee8cc05448795ea38f`. All required and post-merge
  checks passed.
- **Release:** Host 0.1.1 was finalized by
  [run 34446544380](https://github.com/FS-GG/.github/actions/runs/34446544380),
  verified on both feeds, and published as the immutable
  [`telemetry-host/v0.1.1`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.1.1)
  release. The prepared archive SHA-256 is
  `da004f32f2293539d043e03ac86e33293b57ab1dddd1a258e2b7401fc066acb7`,
  the release-manifest SHA-256 is
  `940c9c5572b32da1747d1ba06bce147c38cafbf8bd1bdddc0a0826a467e5c5e2`,
  the nuget.org signed outer archive SHA-256 is
  `92357f9457ba9ce3f9beedb7eda8256d9c7cee419b6363cabc6244aaa7fc577e`,
  and both archives normalize to producer payload
  `sha256:2bf8d0c3d0be3df545be66ff7ba67eb98ad5d2df157cff5417ceb2176d36dc2a`.
  The immutable 0.1.0 release remains available as its distinct earlier source
  and payload.
- **Deployment rehearsal:** the accepted
  [SystemAdmin 0.1.1 consumer](https://github.com/EHotwagner/SystemAdmin/pull/8),
  merge `1117ab4f3a0507f559643a3f85af3c8ba771e2cf`, downloaded and verified the
  exact release assets. Its
  [rootless Podman run 34446689525](https://github.com/EHotwagner/SystemAdmin/actions/runs/34446689525)
  built from the digest-pinned ASP.NET Core 10 image and exercised real
  producer/browser authentication, durable receipt application, abrupt
  container kill and recovery, stopped-container recreation, coherent backup,
  fresh restore and restored receipt readback on an eligible isolated bind
  filesystem. Failed candidate staging retained the exact installed image;
  same-version rollback and future schema-10 refusal preserved the schema-9
  source. The same rootless journey passed again from the accepted merge in
  [post-merge run 34446839361](https://github.com/EHotwagner/SystemAdmin/actions/runs/34446839361).

H2 is complete for immutable packaging and the selected isolated deployment
rehearsal. The run did not operate Main, did not test 0.1.1-to-0.1.0 rollback,
and did not simulate physical power loss. Main route/TLS/firewall identities,
service supervision, backup retention/RPO/RTO, container-to-Main reachability,
cross-version rollback and power-loss behavior remain H3 evidence.
