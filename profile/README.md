# FS-GG

> [!WARNING]
> **A major rework is ongoing. Many components and workflows may be incomplete or non-functional.**
> Feature descriptions, setup examples, and availability claims across the documentation are stale
> and may describe earlier implementations or planned behavior. They do not establish current
> functionality. Check the relevant repository's recent changes, release evidence, and known issues
> before adopting a component.

FS-GG is a collection of F# tools and libraries for application development. Its scope includes
workspace templates, agent-assisted development, GitHub coordination, and UI, game, audio, and
networking components. Availability and integration vary by component during the rework.

> [!NOTE]
> The [FS-GG Unified Development Roadmap](https://github.com/FS-GG/.github/blob/main/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md)
> tracks the rework. Its [current progress report](https://github.com/FS-GG/.github/blob/main/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#0-current-progress-report)
> links completed evidence, active work, and remaining requirements. A completed implementation
> does not by itself establish that a component is published, configured, or working in a particular workspace.

## Platform areas under revision

These areas describe the platform's scope, not a verified list of working features:

- **Workspace creation:** templates, repository setup, and provider integration.
- **Spec-driven development (SDD):** specifications, implementation, validation, and delivery.
- **Agent coordination:** GitHub boards, task ownership, parallel work, and cross-repository dependencies.
- **Formal models:** adoption of Quint, an executable specification language for modeling state,
  transitions, and properties.
- **Application components:** UI, game, audio, networking, and optional governance packages.
- **Telemetry:** development activity and configured runtime observations; coverage depends on
  collection and publication being active.

## Documentation and repository status

- [Documentation index](https://github.com/FS-GG/.github/blob/main/docs/design-goals/README.md) — guides and reference material; check dates and implementation evidence.
- [Architecture](https://github.com/FS-GG/.github/blob/main/docs/architecture.md) — component relationships and design boundaries.
- [Workspace templates](https://github.com/FS-GG/FS.GG.Templates) — template source, releases, and issues.
- [SDD](https://github.com/FS-GG/FS.GG.SDD) — development lifecycle source, releases, and issues.
- [Repositories](https://github.com/orgs/FS-GG/repositories) — component-specific code and maintenance activity.
- [Telemetry dashboard](https://fs-gg.github.io/.github/) — published observations; inspect source timestamps and coverage before drawing conclusions.

## Agent access to GitHub work

Board automation can act on issues and Project data. Restrict modification rights to trusted actors.
Public GitHub content remains untrusted input; the
[trust boundary](https://github.com/FS-GG/.github/blob/main/docs/coordination/untrusted-content-boundary.md)
defines how agents must handle it.

## License

MIT.
