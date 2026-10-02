# Persistent v3 inactive image closure

This directory contains the additive P2-C.3 source boundary. `ImageClosure.prepare` is a pure constructor: it admits one closed set of Host 0.3, served manager bundle, ASP.NET 10.0.12 substrate, native executable, verifier, profiles, and complete canonical inventory. It produces deterministic inactive context metadata with UID 32768 and no service or activation authority.

`production-selection.json` records the identities already acquired from protected sources. Its native closure inventory remains `acquisition-required`, so the CLI exits 2 and emits no context. A later qualification window must acquire and bind that inventory before either isolated image build.

`PersistentV3Runner.qnt` is the lifecycle authority for the later two-build qualification. `Runner.fs` implements the same transitions. The runner owns exactly two build stores, preserves unknown outcomes after lost acknowledgements, cancellation, stale results, or cleanup failure, and recognizes success only after matching results, fresh input, accepted qualification, and observed cleanup.

This source does not build an image, create a container, install a runtime, grant access, activate collection, or change the existing persistent container definitions.
