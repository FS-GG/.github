# GS2-08.8 shared receiver proof

`run.sh` downloads the exact anonymous nuget.org 0.90.0 coherent set and its immutable release manifest,
then supplies those files plus the receiver's explicit Git revision and tree to `run.py`. The verifier binds
the public CLI and Kit archives to their release payloads, binds Drivers through the coherent release manifest,
checks the selected receiver pin, installs
the CLI into an isolated tool directory, and proves that fake credentials cannot make the unavailable
production fence reach an ephemeral loopback provider.

The checked receiver report uses the shared `fsgg.gs2-08.8-receiver-adoption/1` shape: a receiver string,
release and package identities, installed command, `routes`, `callableRoutes`, qualification, and the remaining
boundary. `schema-contract.json` records those required top-level fields and the closed route dispositions
`bridge-adopted`, `read-only-local-only`, and `gs2-08.9-sealing`. Behavioral detail belongs in `behavior`,
`reason`, or qualification fields; it does not create another disposition.

The receiver revision and tree are qualification inputs. They are deliberately not predicted in the candidate
report, so a receiver can use the proof before its future merge identity exists. Coordination may bind the
protected merged revision and tree during native acceptance.

```console
bash tests/bridge-receivers/run.sh
```

The self-test removes the receiver, drops an accounted route, restores an old CLI pin, substitutes a public
package, and labels a revision-bound workflow as immutable. Every mutation must be refused.
