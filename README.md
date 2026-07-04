# .github

Organization-level files for the [FS-GG](https://github.com/FS-GG) org.

- [`docs/architecture.md`](docs/architecture.md) — the **architecture guide**: the
  four-component split, the one-way dependency rule, the contract registry, and how
  the repositories compose. Start here for the big picture.
- [`profile/README.md`](profile/README.md) — the org landing page.
- [`docs/consumer/`](docs/consumer/index.md) — the **consumer guide**: install,
  scaffold, run, drive the lifecycle, and optionally govern a workspace built with
  FS-GG (cross-component processes for people *using* FS-GG).
- [`docs/`](docs/) — cross-repo split decision and implementation plans (the
  shared decision record for people *developing* FS-GG). Start at
  [`docs/index.md`](docs/index.md).
- [`dist/dotnet/`](dist/dotnet/) — the org-shared .NET build config (MSBuild props,
  CPM, pinned tool manifest) distributed to every repo via
  [`scripts/sync-build-config.sh`](scripts/sync-build-config.sh). See
  [`docs/build/README.md`](docs/build/README.md).

Component docs live in each component repository; only cross-cutting material lives
here.
