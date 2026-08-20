# Receiver yield, 2026-07-20 through 2026-08-20

## Scope and method

This is a one-off measurement of the coordination apparatus reaching the seven
framework receivers during the half-open UTC window
`[2026-07-20T00:00:00Z, 2026-08-20T00:00:00Z)`. The receivers are
`FS.GG.SDD`, `FS.GG.Rendering`, `FS.GG.Governance`, `FS.GG.Templates`,
`FS.GG.Game`, `FS.GG.Audio`, and `FS.GG.Net`.

Verification: `PATH=<venv-with-PyYAML>/bin:$PATH scripts/repos.sh list
--receives coordination-kit` against `.github` commit `96a2e522`; the command
returns exactly those seven repositories. The authoritative declarations are
also the seven `role: framework` rows carrying `coordination-kit` in
`registry/repos.yml`.

For this report, a **landed kit materialization** is a first-parent commit on a
receiver's `origin/main` during the window at which that tree's effective
`FS.GG.Kit` pin changed. This counts what the receiver accepted, not packages
merely published or pull requests merely opened. It does not claim that every
package transition changed every materialized byte.

The kit transports four versionless skill payloads: `cross-repo-coordination`,
`intra-repo-parallel-work`, `check-board`, and `pnext-item`. Their receiver-side
version boundary is therefore the `FS.GG.Kit` package version; the package's
`kit/kit-manifest.tsv` content-addresses individual files, but no independent
skill SemVer is declared.

Verification: `PATH=<venv-with-PyYAML>/bin:$PATH scripts/repos.sh kit --kind
skill --field id` at `96a2e522` prints those four ids. Verification:
`src/FS.GG.Kit/stage-kit.sh:17-21` defines skill payload staging and the
SHA-256-bearing manifest, while `src/FS.GG.Kit/FS.GG.Kit.csproj:41` declares
the package identity.

The other apparatus package measured here is the receiver-owned
`fs.gg.coord.cli` .NET tool pin. Product packages are outside this report: they
are receiver output or product dependencies, not delivery of the coordination
apparatus.

## What reached each receiver

| receiver | landed kit materializations | kit pin at window end | coord CLI pin: start -> end |
|---|---:|---:|---:|
| FS.GG.SDD | 33 | 0.62.0 | 0.6.0 -> 0.58.0 |
| FS.GG.Rendering | 33 | 0.62.0 | 0.6.0 -> 0.58.0 |
| FS.GG.Governance | 29 | 0.60.0 | 0.6.0 -> 0.58.0 |
| FS.GG.Templates | 18 | 0.58.0 | 0.6.0 -> 0.58.0 |
| FS.GG.Game | 33 | 0.58.0 | 0.6.0 -> 0.58.0 |
| FS.GG.Audio | 32 | 0.62.0 | 0.6.0 -> 0.58.0 |
| FS.GG.Net | 8 | 0.57.0 | absent -> 0.58.0 |
| **total transitions** | **186** | n/a | **51 CLI pin transitions** |

Verification: the following read-only history program was run against a
freshly fetched `origin/main` in each named receiver checkout. It prints the
count and the complete accepted version sequence used for the first two data
columns above:

```bash
repo=/path/to/receiver
prev=absent
count=0
while read -r sha; do
  version="$({
    git -C "$repo" grep -h -E \
      '(PackageVersion|PackageReference) Include="FS.GG.Kit"' \
      "$sha" -- '*.props' '*.proj' 2>/dev/null || true
  } | sed -nE 's/.*Version="([^"]+)".*/\1/p' | head -1)"
  test -n "$version" || version=absent
  if test "$version" != "$prev"; then
    printf '%s %s %s\n' "$sha" "$prev" "$version"
    count=$((count + 1))
    prev="$version"
  fi
done < <(git -C "$repo" rev-list --first-parent --reverse \
  --since='2026-07-20T00:00:00Z' --until='2026-08-20T00:00:00Z' \
  origin/main)
printf 'count=%s end=%s\n' "$count" "$prev"
```

Verification: the per-receiver counts sum to 186 with `printf '%s\n'
33 33 29 18 33 32 8 | awk '{s += $1} END {print s}'`.

Verification: the start and end CLI pins were read from the last first-parent
commit before each boundary with `git rev-list -1 --before=<timestamp>
origin/main`, followed by `git show
<sha>:.config/dotnet-tools.json`; each intermediate transition was enumerated
over the same first-parent commit stream. The per-receiver transition counts
were SDD 7, Rendering 7, Governance 7, Templates 7, Game 8, Audio 8, and Net
7. Verification: `printf '%s\n' 7 7 7 7 8 8 7 | awk '{s += $1} END {print
s}'` returns 51.

### Accepted FS.GG.Kit versions

These are the complete effective pin sequences during the window. Repeated
package versions are not collapsed across receivers because a receiver only
consumes a package when its own main branch accepts the pin.

- **FS.GG.SDD (33):** 0.1.0, 0.2.2, 0.2.3, 0.6.0, 0.8.0, 0.10.0, 0.15.0,
  0.17.0, 0.18.0, 0.19.0, 0.21.0, 0.22.0, 0.23.1, 0.23.2, 0.23.3, 0.24.0,
  0.26.0, 0.27.0, 0.29.0, 0.31.0, 0.35.0, 0.35.1, 0.37.0, 0.39.0, 0.41.0,
  0.42.0, 0.44.0, 0.47.0, 0.49.0, 0.58.0, 0.60.0, 0.61.0, 0.62.0.
  Verification: run the kit-history program above with an FS.GG.SDD checkout;
  the terminal commit is `c5ddf6d73908c6cc21f860d1105cd455e57d1075`.
- **FS.GG.Rendering (33):** 0.1.0, 0.2.0, 0.2.2, 0.2.3, 0.6.0, 0.7.0,
  0.8.0, 0.15.0, 0.17.0, 0.18.0, 0.27.0, 0.29.0, 0.31.0, 0.35.0, 0.35.1,
  0.37.0, 0.39.0, 0.41.0, 0.42.0, 0.43.0, 0.44.0, 0.46.0, 0.47.0, 0.49.0,
  0.50.0, 0.50.2, 0.51.1, 0.52.0, 0.57.0, 0.58.0, 0.60.0, 0.61.0, 0.62.0.
  Verification: run the kit-history program above with an FS.GG.Rendering
  checkout; the terminal commit is
  `4dd769cd1b6748e7a68b70bfda4c1170c386d50f`.
- **FS.GG.Governance (29):** 0.1.0, 0.2.2, 0.2.3, 0.6.0, 0.7.0, 0.8.0,
  0.15.0, 0.17.0, 0.18.0, 0.19.1, 0.21.0, 0.22.0, 0.23.1, 0.23.2, 0.23.3,
  0.24.0, 0.26.0, 0.27.0, 0.29.0, 0.31.0, 0.47.0, 0.49.0, 0.50.0, 0.50.2,
  0.51.1, 0.52.0, 0.57.0, 0.58.0, 0.60.0.
  Verification: run the kit-history program above with an FS.GG.Governance
  checkout; the terminal pin commit is
  `1f70698db1793894577093bf1b61e15d2028fe01`.
- **FS.GG.Templates (18):** 0.1.0, 0.2.2, 0.2.3, 0.4.0, 0.6.0, 0.8.0,
  0.15.0, 0.17.0, 0.18.0, 0.21.0, 0.22.0, 0.23.1, 0.23.2, 0.23.3, 0.24.0,
  0.26.0, 0.47.0, 0.58.0.
  Verification: run the kit-history program above with an FS.GG.Templates
  checkout; the terminal commit is
  `0368f9fc6027b27aa9976b86490feb2bfa9ced97`.
- **FS.GG.Game (33):** 0.1.0, 0.2.2, 0.2.3, 0.6.0, 0.7.0, 0.8.0, 0.15.1,
  0.17.0, 0.18.0, 0.19.0, 0.21.0, 0.22.0, 0.23.1, 0.23.2, 0.23.3, 0.24.0,
  0.26.0, 0.27.0, 0.29.0, 0.31.0, 0.35.0, 0.35.1, 0.37.0, 0.39.0, 0.41.0,
  0.42.0, 0.43.0, 0.44.0, 0.46.0, 0.47.0, 0.48.0, 0.49.0, 0.58.0.
  Verification: run the kit-history program above with an FS.GG.Game checkout;
  the terminal commit is `50f77442598d6f5bb19bc7d336323e203d74a10f`.
- **FS.GG.Audio (32):** 0.1.0, 0.2.3, 0.5.0, 0.6.0, 0.15.0, 0.17.0,
  0.18.0, 0.19.1, 0.21.0, 0.22.0, 0.23.1, 0.23.2, 0.24.0, 0.26.0, 0.29.0,
  0.31.0, 0.35.0, 0.35.1, 0.37.0, 0.39.0, 0.41.0, 0.42.0, 0.43.0, 0.44.0,
  0.46.0, 0.47.0, 0.49.0, 0.50.0, 0.58.0, 0.60.0, 0.61.0, 0.62.0.
  Verification: run the kit-history program above with an FS.GG.Audio checkout;
  the terminal commit is `06c98363d96bc79ed04b6751015ed708a482c01f`.
- **FS.GG.Net (8):** 0.1.1, 0.2.3, 0.6.0, 0.8.0, 0.15.0, 0.17.0, 0.18.0,
  0.57.0.
  Verification: run the kit-history program above with an FS.GG.Net checkout;
  the terminal pin commit is
  `53624048319ee1d906d6b24c39a35a7605d1314c`.

## Prevention yield and escaped incidents

| receiver | coordination defects caught before receiver impact | receiver incidents the apparatus failed to prevent |
|---|---|---|
| FS.GG.SDD | unverified | unverified |
| FS.GG.Rendering | unverified | unverified |
| FS.GG.Governance | unverified | unverified |
| FS.GG.Templates | unverified | unverified |
| FS.GG.Game | unverified | unverified |
| FS.GG.Audio | unverified | unverified |
| FS.GG.Net | unverified | unverified |

Verification: unverified. The repositories retain gate results, issues, and
pull requests, but no durable record in the measured window binds a particular
coordination-gate rejection to (a) a demonstrated defect, (b) a repair before
receiver impact, and (c) the receiver that would otherwise have been affected.
Likewise, no typed field distinguishes a receiver incident caused by a gap in
this apparatus from an ordinary receiver defect. Text searches can produce
candidates, but counting those candidates would estimate causality and would
therefore overstate what the evidence establishes.

## What this report cannot establish

This report establishes accepted package transitions from immutable main-branch
history. It cannot establish prevention yield, escaped-incident yield, the
amount of receiver work enabled by any delivered skill, or whether multiple
rapid version transitions represented useful receiver value rather than
transport churn. It also does not measure packages published but never consumed,
open update pull requests, or receiver work delivered through product-specific
contracts outside `FS.GG.Kit` and `fs.gg.coord.cli`. Those boundaries are
intentional: substituting search-result counts or inferred causality would make
the measurement look complete when it is not.

This report draws no conclusion about whether the coordination apparatus is
warranted. It supplies the measured delivery side and leaves that judgement to
the operator.
