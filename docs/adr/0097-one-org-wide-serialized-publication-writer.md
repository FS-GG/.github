# ADR-0097: One org-wide serialized publication writer

- **Status:** Accepted
- **Date:** 2026-10-24
- **Affects:** every FS-GG repository that publishes a package, tag or release; `.github` publishing workflows
- **Decision owner:** repository owner (delegation recorded by Programme/root)
- **Decision custody:** [FS-GG/.github#4753 comment 6385677196](https://github.com/FS-GG/.github/issues/4753#issuecomment-6385677196)
- **Amended by:** [ADR-0098](0098-per-resource-publication-claims-for-disjoint-writers.md) (Proposed 2026-10-30; not in force. This record's "no parallel publication" rule governs until ADR-0098 is accepted and qualified.)

## Context

Publishers in different repositories write to shared resources: the two feeds, the Authority
journal and Authority `main`. Journal compare-and-swap rejects a conflicting journal update, but it
does not fence two runs from interleaving package, tag and release writes. Each publisher therefore
carried its own exclusion: a per-repository `concurrency` group in the three `.github` writers, and
operator-held merge freezes elsewhere.

GitHub `concurrency` groups are scoped to **one repository**. A group name shared by workflows in
two repositories does not serialize them. Root decided on one org-wide serialized writer, replacing
an aggregate-resource profile for disjoint writers, so a real cross-repository mechanism is needed.

## Decision

**All publishing jobs serialize through one org-wide publication writer lock.** There is no
parallel publication and no per-resource reservation.

The mechanism is a bakery lock over the public Actions job list
(`scripts/publication_writer_lock.py`, composite action `.github/actions/publication-writer-lock`):

- A *participant* is a job whose display name contains `[org-publication-writer]`, in a workflow
  listed in `registry/publication-writers.json`, which each job reads live from `FS-GG/.github`
  `main`. Identification uses job-level fields only (name, status, `started_at`, runner): GitHub's
  per-step list is often empty even for running jobs, so it must never decide who participates.
  Each such job also carries a step named `Acquire org publication writer lock`.
- Its *ticket* is the job's `started_at`, then the job id. GitHub sets it when the runner takes the
  job, so it cannot be renewed or reordered. A job without `started_at` is never ticketed.
- A job proceeds only when no other `in_progress` participant, in any registered repository, holds a
  smaller ticket. Waiting is only on strictly older jobs, so the oldest live participant always
  proceeds and a cycle is impossible.
- **The lock is the job.** There is no release step. When the job ends in any way (success, failure,
  cancellation, runner loss or GitHub's timeout) it stops being `in_progress` and the next ticket
  proceeds. A crashed holder cannot deadlock the org.
- Bounded recovery: a job still listed in progress after seven hours (above GitHub's six-hour
  hosted-job limit) is ignored as a ghost; a waiter gives up after `max-wait-minutes` and fails
  closed. An unreadable registry, listing or own-job record never acquires.

The lock fences concurrent *writers* only. It grants no authority: candidate identity, journal CAS,
revocation, protection, the live-main equality guard and every effect guard decide whether an
effect may happen, exactly as before.

**Why this mechanism.** It is the only one that needs no new credential. A producer's
`GITHUB_TOKEN` cannot write to another repository, producer workflows cannot move into one repository
because the nuget.org Trusted Publishing policy binds the workflow's own repository and filename, and
the ledger App credentials exist only in `.github` and one `FS.GG.Coordination` environment, not in the producer repositories. The job list is public, already authoritative about
which jobs are alive, and is released by GitHub itself when a job dies.

**Replaced.** The per-repository publisher groups `release-successor-publish`,
`release-telemetry-host-successor-publish` and `release-new-sdd-workspace-successor-publish`.
Both mutation-capable Wizard jobs, the Telemetry Host publisher, the coherent publisher and its
install-only suspension job now take the lock; the read-only `recovery-diagnostic` job does not.
Producer-local groups that only de-duplicate one tag's run are kept; they are not the writer
exclusion.

## Consequences

- A second publication dispatched while another is running waits inside its lock step, up to its
  bounded wait, instead of being queued or silently replaced by GitHub's single-pending-run rule.
- The lock fences write-capable *jobs*, not whole releases. A multi-job publication (for example
  Game wasm or Rendering) is ordered by its own `needs` chain; another publisher's job may run between
  two of its jobs, as no two write-capable jobs ever overlap. Jobs that are read-only by contract are
  listed under the registry row's `observationJobs` and take no lock.
- A reusable workflow carries the gate in its own jobs; its callers are registered too (a run's
  `path` is the caller's) and pass the audit because they hold only `uses:` jobs. A publisher
  workflow must register in `registry/publication-writers.json` and carry the step
  before its first effect. `scripts/check-publication-writer-coverage.py --local` is a required
  selftest; `--remote` reports unadopted repositories.
- Waiting is queueing, not work. `scripts/check-workflow-timeouts.py` raises the 60 minute ceiling
  of a job by exactly its declared `max-wait-minutes`, and the `.github` writer jobs raised their
  `timeout-minutes` by the same amount (publish 60 to 90, recovery-complete 28 to 48, install-only
  suspension 15 to 45), so a job keeps its original working budget. The publisher's operation clocks
  start after the lock is held. Reads retry 5xx and rate limiting with backoff, then fail closed.
- Visibility lag in the job list is absorbed by a 20 second settle and a second clear scan. If
  GitHub's list lagged by longer than that, two jobs could overlap; the journal CAS and per-effect
  guards remain the fence in that case, as they were before this lock.
- Renaming a gated job without the marker removes it from the lock for everyone else.
  `check-publication-writer-coverage.py` requires the marker, the gate step, and nothing but checkout
  before the gate, in every write-capable job of a registered workflow; `--local` is a required
  selftest here and `--remote` (scheduled weekly) reports drift in other repositories.
- The one-use `.104` continuation route (`continue_original_0104_checkpoint`, still a dispatchable
  input, gated by the installed release suspension) has a census in
  `scripts/release-successor-publish.py` (`continuation_native_census`)
  that refuses while any other run of that workflow is in progress or
  queued. A second dispatch now waits `in_progress` in its lock step, where the old group left it
  pending, so an operator must not dispatch a second publisher while using that one-use route. It
  refuses; it does not publish. The script is authentication-pinned and was not edited.
- Pinned-script form. `FS.GG.Coordination`'s Actions allow-list admits only GitHub-owned actions and one
  `NuGet/login` digest, and it is not widened. Its dispatch-only publishers therefore take the same lock with a
  plain `run:` step that fetches `scripts/publication_writer_lock.py` from `FS-GG/.github` at a pinned commit,
  verifies a pinned sha256 and executes it (the identical script the composite action runs). The coverage
  checker accepts, outside this repository, either the pinned composite action or exactly that step, and `--remote` also proves every pinned
  commit exists and its script has the pinned digest. A pin is moved only by a reviewed change in the adopting
  repository, so a protocol change to the lock needs a repin there. A gated job in a repository missing from the
  registry fails closed.
- Not covered until adopted: repositories not yet registered. The GS2-08.9 historical `.github` saga routes are
  exempt because their publishers are `disabled_manually`, a mutable setting. `RecoilEngine` is exempt as not an
  FS-GG component.
- The live-main equality guard and the "hold `main` stable" practice remain. They bind a candidate to
  its exact source; the lock does not relax them (#4753 clause 1).

## Alternatives considered

- **GitHub `concurrency` group.** Repository-scoped; cannot serialize across repositories.
- **A lock record in the Authority journal (CAS).** Sound, but it needs the ledger App token in every
  producer repository, which does not exist and which this decision may not mint.
- **Route every publisher through one central workflow or repository.** Breaks the nuget.org Trusted
  Publishing policy binding and moves each producer's guards; far larger than the lock.
- **Dispatch to a central queue.** Needs a cross-repository dispatch credential; `dispatch-sender` is
  validation-only, though revoking the shared App's installation is recorded as outside this repository
  and was not verified here.
- **Aggregate-resource profile for disjoint writers.** The earlier option; replaced by this decision.
  *2026-10-30 note:* [ADR-0098](0098-per-resource-publication-claims-for-disjoint-writers.md) proposes reopening
  it as per-resource claims layered on this lock. It is Proposed only; nothing here changes until it is accepted.
