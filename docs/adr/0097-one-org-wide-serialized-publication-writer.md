# ADR-0097: One org-wide serialized publication writer

- **Status:** Accepted
- **Date:** 2026-10-24
- **Affects:** every FS-GG repository that publishes a package, tag or release; `.github` publishing workflows
- **Decision owner:** repository owner (delegation recorded by Programme/root)
- **Decision custody:** [FS-GG/.github#4753 comment 6385677196](https://github.com/FS-GG/.github/issues/4753#issuecomment-6385677196)

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

- A *participant* is a job containing a step named exactly `Acquire org publication writer lock`
  in a workflow listed in `registry/publication-writers.json`, which each job reads live from
  `FS-GG/.github` `main`.
- Its *ticket* is the time GitHub started its runner, then the job id. GitHub fixes it before any
  step runs, so it cannot be renewed or reordered.
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
the ledger App key is held by `.github` alone. The job list is public, already authoritative about
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
- A publisher workflow must register in `registry/publication-writers.json` and carry the step
  before its first effect. `scripts/check-publication-writer-coverage.py --local` is a required
  selftest; `--remote` reports unadopted repositories.
- The wait is bounded below the job timeout, so waiting consumes part of a job's own clock. The
  publisher's operation clocks start after the lock is held.
- Visibility lag in the job list is absorbed by a 20 second settle and a second clear scan. If
  GitHub's list lagged by longer than that, two jobs could overlap; the journal CAS and per-effect
  guards remain the fence in that case, as they were before this lock.
- Not covered until adopted: repositories not yet registered, and `FS.GG.Coordination`, whose
  Actions allow-list admits only GitHub-owned actions and one `NuGet/login` digest. Its four
  publishing workflows are exempt in the registry with that reason.
- The live-main equality guard and the "hold `main` stable" practice remain. They bind a candidate to
  its exact source; the lock does not relax them (#4753 clause 1).

## Alternatives considered

- **GitHub `concurrency` group.** Repository-scoped; cannot serialize across repositories.
- **A lock record in the Authority journal (CAS).** Sound, but it needs the ledger App token in every
  producer repository, which does not exist and which this decision may not mint.
- **Route every publisher through one central workflow or repository.** Breaks the nuget.org Trusted
  Publishing policy binding and moves each producer's guards; far larger than the lock.
- **Dispatch to a central queue.** Needs a cross-repository dispatch credential; the shared App is retired.
- **Aggregate-resource profile for disjoint writers.** The earlier option; replaced by this decision.
