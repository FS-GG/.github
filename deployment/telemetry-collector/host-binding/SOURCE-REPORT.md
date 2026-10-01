# OPS-TYPED-01.5 HOST binding source report

**Status:** source tested on 2026-10-01. Protected recipe adoption, Sandbox
rendering, wrapper adoption, and a native attempt remain pending root work.

## Source result

The new nonpackable .NET 10 F# executable constructs a stateless
`ValidatedHostBinding` from the current clean recipe revision and tree, the raw
native profile bytes, the closed four-entry native source-pin document, each
matching working-tree and Git blob, and the compiled producer DLL. It parses the
profile and pins with duplicate-key, field-set, scalar-kind, digest, operation,
and size bounds. The profile bytes are hashed without reserialization.

`inspect` and `render` disclose only public identities. `derive` retains the
existing UTF-8 SHA-256 formula over nonce, source revision, profile digest, and
operation ID separated by NUL bytes. `verify` reconstructs the snapshot, compares
its binding and the admission in constant time, and accepts the candidate only on
standard input. The executable retains no authorization state.

`qualify_native_container.py` now invokes that compiled executable during
preflight and again immediately before materialization. The Python adapter no
longer contains the profile digest or admission formula. A refusal leaves the
synthetic authentication environment untouched and creates no private root.
Existing container, network, custody, cleanup, native producer, and source-input
behavior is unchanged.

## Verification

- locked restore and Release build: passed with .NET SDK 10.0.401;
- F# constructor tests: 9 passed;
- private native qualification tests: 33 passed with the real compiled producer;
- current raw profile digest `1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34`: accepted;
- predecessor profile digest `5a30fc507f023d542521aac66c8f49c5ae6ee8d9e34dc90c1a3bf3ab30f6b08f`: refused;
- independent admission vector, nonce/source/profile/operation and binding drift,
  source mutation, duplicate keys, wrong kinds, wrong operation, wrong revision,
  mixed pins, and unsupported fields: covered;
- Python compile and `git diff --check`: passed;
- all four native public-input source blobs are byte-identical to base
  `e5582c6c9b049e325dde486e5bd6a32668a3a8c4`.

## Root integration boundary

`/tmp/ops-typed-01-5-private-native-qualification-root.patch` is a prepared,
uncommitted shared-template patch. It replaces the manual profile slot with the
renderer slot, pins the existing setup-dotnet v6 commit and SDK 10.0.401, performs
a locked Release build before the credential-bearing step, validates the rendered
recipe/profile tuple, and passes the compiled verifier to the operation adapter.

The Sandbox workflow renderer, private wrapper successor, qualified compiled
artifact identity, protected recipe, and actual source/effect attempt are not
provided by this source candidate. The unchanged four native input files permit
retaining release `401047616` only after root verifies those bytes at the final
protected recipe revision. No installed or native qualification is claimed.
