# .github

Organization-level files for the [FS-GG](https://github.com/FS-GG) org.

- [`profile/README.md`](profile/README.md) — the org landing page.
- [`docs/consumer/`](docs/consumer/index.md) — the **consumer guide**: install,
  scaffold, run, drive the lifecycle, and optionally govern a product built with
  FS-GG (cross-product processes for people *using* FS-GG).
- [`docs/`](docs/) — cross-repo split decision and implementation plans (the
  shared decision record for people *developing* FS-GG). Start at
  [`docs/index.md`](docs/index.md).
- [`dist/dotnet/`](dist/dotnet/) — the org-shared .NET build config (MSBuild props,
  CPM, pinned tool manifest) distributed to every repo via
  [`scripts/sync-build-config.sh`](scripts/sync-build-config.sh). See
  [`docs/build/README.md`](docs/build/README.md).

Product docs live in each product repository; only cross-cutting material lives
here.
