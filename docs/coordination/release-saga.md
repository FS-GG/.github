# Coherent-set release saga

`FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers` form one stable coherent set. Independent
registries cannot provide a shared transaction, so publication is a resumable saga whose durable
truth is the content-addressed `release-manifest.json` attached to `coherent-set/v<version>`.

## Prepare

Run `release-saga-prepare` at the exact merged source SHA. It builds and tests the engine, packs each
member exactly once, reads each nuspec and dependency relation, hashes every archive and payload,
preflights both feeds' package-size and release-notes limits, then creates a draft release containing
the three archives and manifest. A repeated prepare accepts only byte-identical assets; it never
clobbers drift under an existing version.

Preparation is reversible: delete the draft before any package tag is pushed. It is not a release and
does not move the stable channel.

## Resume publication

Push `kit/v<version>`, `drivers/v<version>`, and `coord-engine/v<version>` together at the prepared
source SHA. The existing package-owning workflows keep their trusted-publishing policy identities.
They download the full set from the draft, validate release/version/source/policy
identity and every artifact hash, and observe their target before writing. Each package publishes to
GitHub Packages and persists its own non-racing journal asset. Every workflow then waits until all
three manifest-bound org packages are externally verified; only beyond that complete-set barrier may
any workflow request its package-specific NuGet token and send the same file to nuget.org. They never
pack during the irreversible phase.

On retry, each package workflow resumes its durable `journal-<package>.json` and inspects both feeds.
A served package is progress only when its payload
matches the prepared artifact; archive hashes are retained separately because nuget.org may append a
signature and regenerate package-services metadata. Missing packages resume from the manifest-bound
archive. A different local hash or externally served payload is a terminal byte-drift refusal, not a
`--skip-duplicate` success.

Published NuGet versions and release tags are immutable and have no rollback operation. Recovery is
forward-only: finish missing feed steps with the same bytes, or—if a registry contains conflicting
bytes—leave the draft unpromoted, record the poisoned version, and prepare a new coherent version.

## Promote

`release-saga-promote` observes package bytes directly from both feeds after each successful package
workflow. It updates per-package external archive/payload hashes and the retry journal, but publishes
the draft as the stable release only after every member is verified on GitHub Packages and nuget.org.
Its `stable-channel.json` receipt binds the version, source SHA, and manifest content ID. Re-running
promotion for the same content is idempotent.

## Rehearsal and recovery

Preparation is the only rehearsal boundary: it packs once into a reversible draft and performs every
preflight without publishing a package. Package-owning workflows have no local-pack or non-publish
mode. A manual dispatch is an exact-source recovery operation and must consume the prepared manifest
and its verified bytes. The superseded release-train coordinator and publisher dry-run arms were
retired at M6; the manifest, journals, observers, trusted publisher identities, and promotion barrier
are the sole operational release boundary.
