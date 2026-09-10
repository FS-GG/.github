# Standalone telemetry H2 source and package evidence

Date: 2026-09-10. Scope: H2 source preparation for the optional standalone
telemetry host. Publication and Main deployment remain separate protected
operations.

## Delivered source boundary

The candidate provides `FS.GG.Telemetry.Host` 0.1.0 as an independently
versioned Linux x64 .NET tool. It includes the Host, Contracts, Store, private
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
recovery therefore depends on the companion SystemAdmin backup of the canonical
config template, TLS material, producer secrets, and browser key-hash files.
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
- Independent release-manifest fixture: 9 passed, including the shared
  `release-saga.py` producer-payload vector, signed-archive distinction, changed
  payload refusal, tag isolation, and foreign-journal refusal.

The packaging lane also prepared a prerelease helper candidate from source
`bcabcac90847ffc3132f0270aeed00a17ae4bbdc` with archive SHA-256
`f57603390fc87224f8071f2599a2b25e20c6b2ce9ac8d80742daaccbb0e1c0f6` and
producer payload SHA-256
`e6f6098c9efd02d01a8cfd0b07bf8a912d52ca554619d5369372a1063647d3fd`.
Those bytes prove the package path during development; they are not the final PR
artifact or a published release because their source binding predates the final
candidate.

## Separate completion states

- **Source:** candidate implementation and the checks listed above are complete;
  acceptance waits for the exact-head routine PR, deterministic two-root package
  proof, and its required CI checks.
- **Release:** pending. The protected `release-telemetry-host.yml` workflow must
  build from accepted `main`, pass Trusted Publishing authority, publish the
  verified producer payload to both feeds, and create
  `telemetry-host/v0.1.0`. No release is claimed here.
- **Deployment:** pending. Main route/TLS identities, qualified storage,
  service activation, authenticated producer/browser journeys, companion config
  recovery, restart, backup/restore, and rollback rehearsal require the exact
  released package and SystemAdmin authority. The H2 roadmap checkbox remains
  open until that operational exit is real.
