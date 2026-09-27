## Summary

Land the first FSC-05 provider source cut from the preserved Templates draft lineage. This introduces the pure F# provider composition tool and its tests, and hardens the Python and F# descriptor and registry floor boundaries through original #538. It includes the independent #498 provider floor structure guard, reconciled where its Python parser changes overlap the provider chain.

## Preserved source

Original provider PRs: #497, #499, #508–#510, #512–#516, and #524–#538. Independent floor guard: #498. The original #538 and #498 heads are exact ancestors of this branch. The merge base is current `main` at `e4355122d83953fe0c3c396ce15e335a5c7e1e27`. Later provider archive, custody, and payload hardening remains in the separate preserved lineage.

## Acceptance

- `python3 scripts/check-provider-floors.py --self-test` — 28/28 passed.
- `python3 tests/provider-floor-structure/run.py` — 13/13 passed.
- All `tests/provider-*/run.py` fixtures — 22/22 passed.
- `bash tests/provider-tool/run.sh` — 29/29 passed.
- `dotnet run --project tests/ProviderComposition.Tests -c Release` — passed.
- `git diff --check origin/main..HEAD` — passed.

This local candidate has no hosted protected checks. Recheck its exact head against current `main` during guarded admission.
